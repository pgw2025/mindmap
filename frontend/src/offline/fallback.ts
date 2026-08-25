/**
 * 网络优先、本地快照兜底（docs/offline-mindmap-design.md §6）
 *
 * - 在线：走网络；onSuccess 回调可增量回写 IDB（单图粒度保持快照新鲜）
 * - 网络异常：读 IDB 快照；本地也没有 → 抛出原错误，UI 走原有空态/报错逻辑
 */
export interface FallbackOptions<T> {
  /** 网络请求成功后的回调（用于增量回写 IndexedDB） */
  onSuccess?: (data: T) => void | Promise<void>
  /** 命中本地快照兜底的回调（用于 UI 标注「离线数据」） */
  onFallback?: (data: T) => void
}

export async function withFallback<T>(
  network: () => Promise<T>,
  cached: () => Promise<T | null>,
  opts: FallbackOptions<T> = {}
): Promise<T> {
  try {
    const data = await network()
    await opts.onSuccess?.(data)
    return data
  } catch (err) {
    const local = await cached().catch(() => null)
    if (local !== null && local !== undefined) {
      opts.onFallback?.(local)
      return local
    }
    // 本地也没有快照 → 维持原错误，让调用方走空态
    throw err
  }
}
