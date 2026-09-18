import { resolveNodeInk, type MindMapThemeConfig, type NodeLevelKey, type ResolvedNodeInk } from './presets'

/**
 * 节点墨色的解算与落地。
 *
 * 背景见 presets.ts 的 resolveNodeInk：simple-mind-map 里文字色与底色是两条独立
 * 的解析链，用户只点选底色时文字色仍继承主题层级色，深色下就会「亮底 + 亮字」。
 * 这里是解算结果落到画布的公共层，编辑器与分享页共用同一份规则，
 * 避免两处各写一套导致分享出去的链接和编辑时看到的配色不一致。
 */

/** 参与墨色解算所需的最小节点形状（后端 NodeDto 与分享接口的节点都满足） */
export interface InkNodeLike {
  id: string | number
  parentId?: string | null
  color?: string | null
  backgroundColor?: string | null
}

/** 深度 → 主题层级（与 simple-mind-map 的 Style.merge 分支一一对应） */
export function nodeLevelOfDepth(depth: number): NodeLevelKey {
  return depth === 0 ? 'root' : depth === 1 ? 'second' : 'node'
}

/**
 * 按 parentId 链计算每个节点的深度，并映射到主题层级。
 * 用 parentId 而不是数组顺序：接口不保证父节点一定排在子节点之前。
 */
export function buildNodeLevelMap(nodes: InkNodeLike[]): Map<string, NodeLevelKey> {
  const byId = new Map<string, InkNodeLike>()
  for (const n of nodes) byId.set(String(n.id), n)

  const levels = new Map<string, NodeLevelKey>()
  const depthCache = new Map<string, number>()

  for (const n of nodes) {
    const startKey = String(n.id)
    let depth = depthCache.get(startKey)
    if (depth === undefined) {
      depth = 0
      let cur: InkNodeLike | undefined = n
      const seen = new Set<string>()
      while (cur && cur.parentId != null && !seen.has(String(cur.id))) {
        seen.add(String(cur.id))
        const parent = byId.get(String(cur.parentId))
        if (!parent) break
        depth++
        cur = parent
      }
      depthCache.set(startKey, depth)
    }
    levels.set(startKey, nodeLevelOfDepth(depth))
  }
  return levels
}

/**
 * 算出所有节点的目标墨色（含是否需要显式注入）。
 * 只有「用户动过底色或文字色、导致这一对脱节」的节点才可能带 injected=true，
 * 主题自己成对设计的层级配色一律不介入，所以普通导图里真正需要写入的节点
 * 通常是个位数甚至零个。
 */
export function computeNodeInkOverrides(
  nodes: InkNodeLike[],
  config: MindMapThemeConfig
): Map<string, ResolvedNodeInk> {
  const result = new Map<string, ResolvedNodeInk>()
  if (nodes.length === 0) return result
  const levelMap = buildNodeLevelMap(nodes)
  for (const n of nodes) {
    const resolved = resolveNodeInk({
      levelKey: levelMap.get(String(n.id)) ?? 'node',
      config,
      explicitInk: n.color,
      explicitFill: n.backgroundColor
    })
    if (resolved) result.set(String(n.id), resolved)
  }
  return result
}

/**
 * 把目标墨色落到已渲染的节点上。
 *
 * 只对与 `injected`（上次写入渲染数据的值）不同的节点调用 SET_NODE_DATA：
 * - 需要修正 → 写入解算值，并记入 injected
 * - 不再需要修正（例如从深色切回浅色）→ 写回候选值（与继承结果一致），并移出 injected
 *
 * 为什么单独做这层补丁、而不是重新 setData：切主题时全量重载会丢视口、
 * 并作废正在挂起的写入。这里 SET_NODE_DATA 不进撤销历史，改文字色也不改变
 * 节点尺寸，因此只重渲受影响的节点，其余节点与视口完全不动。
 *
 * @returns 本次实际写入的节点数
 */
export function patchNodeInkOverrides(opts: {
  /** simple-mind-map 实例 */
  instance: unknown
  overrides: Map<string, ResolvedNodeInk>
  /** backendId → 上次写入渲染数据的文字色 */
  injected: Map<string, string>
}): number {
  const { instance, overrides, injected } = opts
  const inst = instance as {
    renderer?: { root?: unknown }
    execCommand?: (name: string, ...args: unknown[]) => void
  } | null
  const root = inst?.renderer?.root
  if (!inst || !root || overrides.size === 0) return 0

  let patched = 0
  const walk = (node: unknown) => {
    const n = node as {
      getData?: (k?: string) => unknown
      nodeData?: { id?: string; data?: { backendId?: string } }
      children?: unknown[]
    }
    const raw = (n.getData?.('backendId')
      ?? n.nodeData?.data?.backendId
      ?? n.nodeData?.id
      ?? n.getData?.('id')) as string | undefined
    if (raw != null) {
      const key = String(raw)
      const resolved = overrides.get(key)
      if (resolved) {
        const prev = injected.get(key)
        const needWrite = resolved.injected ? prev !== resolved.ink : prev !== undefined
        if (needWrite) {
          inst.execCommand?.('SET_NODE_DATA', n, { color: resolved.ink })
          patched++
        }
        if (resolved.injected) {
          injected.set(key, resolved.ink)
        } else {
          injected.delete(key)
        }
      }
    }
    if (n.children) n.children.forEach(walk)
  }
  walk(root)
  return patched
}
