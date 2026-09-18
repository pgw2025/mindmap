export interface MindMapThemeConfig {
  paddingX: number
  paddingY: number
  imgMaxWidth: number
  imgMaxHeight: number
  iconSize: number
  lineWidth: number
  lineColor: string
  lineDasharray: string
  lineFlow: boolean
  lineFlowDuration: number
  lineFlowForward: boolean
  lineStyle: string
  rootLineKeepSameInCurve: boolean
  rootLineStartPositionKeepSameInCurve: boolean
  lineRadius: number
  showLineMarker: boolean
  generalizationLineWidth: number
  generalizationLineColor: string
  generalizationLineMargin: number
  generalizationNodeMargin: number
  associativeLineWidth: number
  associativeLineColor: string
  associativeLineActiveWidth: number
  associativeLineActiveColor: string
  associativeLineDasharray: string
  associativeLineTextColor: string
  associativeLineTextFontSize: number
  associativeLineTextLineHeight: number
  associativeLineTextFontFamily: string
  backgroundColor: string
  backgroundImage: string
  backgroundRepeat: string
  backgroundPosition: string
  backgroundSize: string
  nodeUseLineStyle: boolean
  root: NodeLevelStyle
  second: NodeLevelStyle
  node: NodeLevelStyle
  generalization: NodeLevelStyle
}

export interface NodeLevelStyle {
  shape: string
  marginX?: number
  marginY?: number
  fillColor: string
  fontFamily: string
  color: string
  fontSize: number
  fontWeight: string
  fontStyle: string
  borderColor: string
  borderWidth: number
  borderDasharray: string
  borderRadius: number
  textDecoration: string
  gradientStyle: boolean
  startColor: string
  endColor: string
  startDir: number[]
  endDir: number[]
  lineMarkerDir: string
  hoverRectColor: string
  hoverRectRadius: number
  textAlign: string
  imgPlacement: string
  tagPlacement: string
}

export interface ThemePreset {
  id: string
  name: string
  description: string
  swatch: { rootFill: string; secondFill: string; lineColor: string; bg: string }
  /** 深色模式下的缩略图配色（卡片封面用） */
  swatchDark: { rootFill: string; secondFill: string; lineColor: string; bg: string }
  config: MindMapThemeConfig
}

/* ============================================================================
 * 明暗派生：把主题拆成「色相 × 明暗」两个正交维度
 * ----------------------------------------------------------------------------
 * - 色相：由预设（THEMES）或模板（configJson）决定，属于这份脑图自己的身份
 * - 明暗：由应用主题（stores/theme.ts 的 isDark）决定，属于当前设备与偏好
 * 深色配置在运行时由 deriveDarkConfig 派生，既不落库也不手写 6 份。
 * 所有对外应用配色的地方都必须走 resolveThemeConfig 这一个出口。
 * ========================================================================== */

/**
 * 兜底深色外壳色，必须与 styles/_theme.scss 中 `.dark` 的
 * --app-bg / --app-card-bg 保持一致：画布底色复用同一族中性色，
 * 画布与页面外壳之间才不会出现「两层深灰」的接缝。
 */
const DARK_SHELL_FALLBACK = { bg: '#0f1419', cardBg: '#181c22' } as const

/** 深色下连线的最低可见亮度（已达标的颜色不再调整） */
const DARK_LINE_LUMINANCE = 0.3
/** 深色下描边的最低可见亮度（1px 发丝线要比连线更亮才看得见） */
const DARK_BORDER_LUMINANCE = 0.45
/** 深色下正文文字的目标亮度 */
const DARK_TEXT_LUMINANCE = 0.62
/** 亮于此值视为「白卡 / 近白卡」，深色下需翻成深色卡 */
const LIGHT_CARD_LUMINANCE = 0.72
/** 底色暗于此值即视为「已适配深色」，派生时原样保留（深色模板、暗夜预设） */
const ALREADY_DARK_BG_LUMINANCE = 0.25
/** 底色亮于此值即视为「浅底」，其上的文字保持原有深浅 */
const LIGHT_BG_LUMINANCE = 0.45

/** 解析 #rgb / #rrggbb；transparent、rgba() 等一律返回 null（视为不可解析） */
function parseHex(color: string | undefined | null): [number, number, number] | null {
  if (typeof color !== 'string') return null
  const raw = color.trim().replace(/^#/, '')
  const hex = raw.length === 3 ? raw.split('').map((c) => c + c).join('') : raw
  if (!/^[0-9a-f]{6}$/i.test(hex)) return null
  return [
    parseInt(hex.slice(0, 2), 16),
    parseInt(hex.slice(2, 4), 16),
    parseInt(hex.slice(4, 6), 16)
  ]
}

function toHex(rgb: [number, number, number]): string {
  return (
    '#' +
    rgb
      .map((v) => Math.max(0, Math.min(255, Math.round(v))).toString(16).padStart(2, '0'))
      .join('')
  )
}

/** 按比例混合两个颜色：t=0 取 a，t=1 取 b；任一端不可解析时返回 a */
export function mixColor(a: string, b: string, t: number): string {
  const ca = parseHex(a)
  const cb = parseHex(b)
  if (!ca || !cb) return a
  const k = Math.max(0, Math.min(1, t))
  return toHex([0, 1, 2].map((i) => ca[i] + (cb[i] - ca[i]) * k) as [number, number, number])
}

/** WCAG 相对亮度；不可解析的颜色返回 null */
export function colorLuminance(color: string | undefined | null): number | null {
  const rgb = parseHex(color)
  if (!rgb) return null
  const [r, g, b] = rgb.map((v) => {
    const c = v / 255
    return c <= 0.03928 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4)
  })
  return 0.2126 * r + 0.7152 * g + 0.0722 * b
}

/** 朝白色逐档提亮到目标亮度；已达标或不可解析则原样返回 */
function lightenTo(color: string, target: number): string {
  if (colorLuminance(color) === null) return color
  let res = color
  for (let i = 0; i < 24 && (colorLuminance(res) ?? 1) < target; i++) {
    res = mixColor(res, '#ffffff', 0.15)
  }
  return res
}

/**
 * 把一份「浅色配置」派生为深色配置。
 * 预设与模板共用同一份规则，所以套了模板的脑图在深色下同样不会破相
 * （白底模板 → 深底 + 浅字，彩色卡片保留色相）。
 * 已经是深色的配置会被识别并保留原样，不做二次翻转。
 */
export function deriveDarkConfig(
  light: MindMapThemeConfig,
  shell: { bg?: string; cardBg?: string } = {}
): MindMapThemeConfig {
  const cfg = JSON.parse(JSON.stringify(light)) as MindMapThemeConfig
  const shellBg = shell.bg ?? DARK_SHELL_FALLBACK.bg
  const shellCardBg = shell.cardBg ?? DARK_SHELL_FALLBACK.cardBg

  // 色相取自连线色（缺失时退回根节点填充色），用于给中性深色染上预设身份
  const hueSource = parseHex(cfg.lineColor) ? cfg.lineColor : cfg.root?.fillColor
  const hue = parseHex(hueSource) ? (hueSource as string) : shellBg

  // 1. 画布底色：与页面外壳同族深色 + 轻微染入色相
  if ((colorLuminance(cfg.backgroundColor) ?? 1) > ALREADY_DARK_BG_LUMINANCE) {
    cfg.backgroundColor = mixColor(shellBg, hue, 0.06)
  }
  const canvasLuminance = colorLuminance(cfg.backgroundColor) ?? 0

  // 2. 连线与辅助线：深色下整体提亮，补偿对比度
  cfg.lineColor = lightenTo(cfg.lineColor, DARK_LINE_LUMINANCE)
  cfg.generalizationLineColor = lightenTo(cfg.generalizationLineColor, DARK_LINE_LUMINANCE)
  cfg.associativeLineColor = lightenTo(cfg.associativeLineColor, DARK_LINE_LUMINANCE)
  cfg.associativeLineActiveColor = lightenTo(cfg.associativeLineActiveColor, DARK_LINE_LUMINANCE)
  cfg.associativeLineTextColor = lightenTo(cfg.associativeLineTextColor, DARK_TEXT_LUMINANCE)

  // 3. 各级节点：白卡翻深卡，再按「实际底色」决定文字与描边的深浅
  const levelKeys = ['root', 'second', 'node', 'generalization'] as const
  levelKeys.forEach((key) => {
    const level = cfg[key]
    if (!level) return

    const fillLuminance = colorLuminance(level.fillColor)
    if (fillLuminance !== null && fillLuminance > LIGHT_CARD_LUMINANCE) {
      level.fillColor = mixColor(shellCardBg, hue, 0.1)
      if (level.gradientStyle) level.endColor = level.fillColor
    }

    // 实际底色：透明填充时露出的就是画布底色
    const effectiveLuminance = parseHex(level.fillColor)
      ? (colorLuminance(level.fillColor) as number)
      : canvasLuminance
    if (effectiveLuminance < LIGHT_BG_LUMINANCE) {
      level.color = lightenTo(level.color, DARK_TEXT_LUMINANCE)
    }
    if (parseHex(level.borderColor)) {
      level.borderColor = lightenTo(level.borderColor, DARK_BORDER_LUMINANCE)
    }
    // 留空表示用库默认的亮色描边（rgb(94,200,248)），深色下依然可见，不动；
    // 只有显式配置了深色值的才需要提亮
    if (parseHex(level.hoverRectColor)) {
      level.hoverRectColor = lightenTo(level.hoverRectColor, DARK_BORDER_LUMINANCE)
    }
  })

  return cfg
}

const DEFAULT_NODE_LEVEL: Omit<NodeLevelStyle, 'fillColor' | 'color' | 'borderColor'> = {
  shape: 'rectangle',
  fontFamily: '微软雅黑, Microsoft YaHei',
  fontSize: 14,
  fontWeight: 'normal',
  fontStyle: 'normal',
  borderWidth: 1,
  borderDasharray: 'none',
  borderRadius: 5,
  textDecoration: 'none',
  gradientStyle: false,
  startColor: '#549688',
  endColor: '#fff',
  startDir: [0, 0],
  endDir: [1, 0],
  lineMarkerDir: 'end',
  hoverRectColor: '',
  hoverRectRadius: 5,
  textAlign: 'left',
  imgPlacement: 'top',
  tagPlacement: 'right'
}

function buildBaseTheme(opts: {
  lineColor: string
  bg: string
  root: Partial<NodeLevelStyle> & { fillColor: string; color: string }
  second: Partial<NodeLevelStyle> & { fillColor: string; color: string; borderColor: string }
  node: Partial<NodeLevelStyle> & { color: string }
  generalization?: Partial<NodeLevelStyle> & { fillColor: string; color: string; borderColor: string }
}): MindMapThemeConfig {
  return {
    paddingX: 15,
    paddingY: 5,
    imgMaxWidth: 200,
    imgMaxHeight: 100,
    iconSize: 20,
    lineWidth: 1.5,
    lineColor: opts.lineColor,
    lineDasharray: 'none',
    lineFlow: false,
    lineFlowDuration: 1,
    lineFlowForward: true,
    lineStyle: 'curve',
    rootLineKeepSameInCurve: true,
    rootLineStartPositionKeepSameInCurve: false,
    lineRadius: 5,
    showLineMarker: false,
    generalizationLineWidth: 1,
    generalizationLineColor: opts.lineColor,
    generalizationLineMargin: 0,
    generalizationNodeMargin: 20,
    associativeLineWidth: 2,
    associativeLineColor: opts.lineColor,
    associativeLineActiveWidth: 8,
    associativeLineActiveColor: '#02a7f0',
    associativeLineDasharray: '6,4',
    associativeLineTextColor: opts.node.color,
    associativeLineTextFontSize: 14,
    associativeLineTextLineHeight: 1.2,
    associativeLineTextFontFamily: '微软雅黑, Microsoft YaHei',
    backgroundColor: opts.bg,
    backgroundImage: 'none',
    backgroundRepeat: 'no-repeat',
    backgroundPosition: 'center center',
    backgroundSize: 'cover',
    nodeUseLineStyle: false,
    root: { ...DEFAULT_NODE_LEVEL, fontSize: 16, fontWeight: 'bold', ...opts.root, borderWidth: 0, borderColor: 'transparent' },
    second: {
      ...DEFAULT_NODE_LEVEL,
      marginX: 100,
      marginY: 40,
      fontSize: 15,
      ...opts.second
    },
    node: {
      ...DEFAULT_NODE_LEVEL,
      marginX: 50,
      marginY: 0,
      fontSize: 14,
      fillColor: 'transparent',
      borderWidth: 0,
      borderColor: 'transparent',
      ...opts.node
    },
    generalization: {
      ...DEFAULT_NODE_LEVEL,
      marginX: 100,
      marginY: 40,
      fontSize: 15,
      ...(opts.generalization ?? opts.second)
    }
  }
}

export const THEMES: ThemePreset[] = [
  {
    id: 'classic',
    name: '清新绿',
    description: '简约自然，经典配色',
    swatch: { rootFill: '#549688', secondFill: '#ffffff', lineColor: '#549688', bg: '#fafafa' },
    swatchDark: { rootFill: '#3d8b75', secondFill: '#1e293b', lineColor: '#549688', bg: '#0f172a' },
    config: buildBaseTheme({
      lineColor: '#549688',
      bg: '#fafafa',
      root: { fillColor: '#549688', color: '#ffffff' },
      second: { fillColor: '#ffffff', color: '#565656', borderColor: '#549688' },
      node: { color: '#6a6d6c' }
    })
  },
  {
    id: 'ocean',
    name: '海洋蓝',
    description: '商务沉稳，清晰专业',
    swatch: { rootFill: '#3b82f6', secondFill: '#eff6ff', lineColor: '#3b82f6', bg: '#f1f5f9' },
    swatchDark: { rootFill: '#2563eb', secondFill: '#1e3a5f', lineColor: '#3b82f6', bg: '#0f172a' },
    config: buildBaseTheme({
      lineColor: '#3b82f6',
      bg: '#f1f5f9',
      root: { fillColor: '#3b82f6', color: '#ffffff' },
      second: { fillColor: '#eff6ff', color: '#1e3a5f', borderColor: '#60a5fa' },
      node: { color: '#475569' }
    })
  },
  {
    id: 'sunset',
    name: '日落橙',
    description: '温暖活力，充满能量',
    swatch: { rootFill: '#f97316', secondFill: '#fff7ed', lineColor: '#fb923c', bg: '#fffbeb' },
    swatchDark: { rootFill: '#ea580c', secondFill: '#431407', lineColor: '#fb923c', bg: '#1c1917' },
    config: buildBaseTheme({
      lineColor: '#fb923c',
      bg: '#fffbeb',
      root: { fillColor: '#f97316', color: '#ffffff' },
      second: { fillColor: '#fff7ed', color: '#7c2d12', borderColor: '#fb923c' },
      node: { color: '#9a3412' }
    })
  },
  {
    id: 'forest',
    name: '森林绿',
    description: '深邃稳重，自然气息',
    swatch: { rootFill: '#15803d', secondFill: '#f0fdf4', lineColor: '#22c55e', bg: '#f7fdf4' },
    swatchDark: { rootFill: '#16a34a', secondFill: '#052e16', lineColor: '#22c55e', bg: '#0f1f14' },
    config: buildBaseTheme({
      lineColor: '#22c55e',
      bg: '#f7fdf4',
      root: { fillColor: '#15803d', color: '#ffffff' },
      second: { fillColor: '#f0fdf4', color: '#14532d', borderColor: '#22c55e' },
      node: { color: '#374151' }
    })
  },
  {
    id: 'cherry',
    name: '樱粉',
    description: '柔和浪漫，温馨舒适',
    swatch: { rootFill: '#ec4899', secondFill: '#fdf2f8', lineColor: '#f472b6', bg: '#fdf4ff' },
    swatchDark: { rootFill: '#db2777', secondFill: '#500724', lineColor: '#f472b6', bg: '#1f1018' },
    config: buildBaseTheme({
      lineColor: '#f472b6',
      bg: '#fdf4ff',
      root: { fillColor: '#ec4899', color: '#ffffff' },
      second: { fillColor: '#fdf2f8', color: '#831843', borderColor: '#f472b6' },
      node: { color: '#9d174d' }
    })
  },
  {
    id: 'midnight',
    name: '暗夜',
    description: '深色模式，夜间护眼',
    swatch: { rootFill: '#6366f1', secondFill: '#312e81', lineColor: '#818cf8', bg: '#1e1b4b' },
    swatchDark: { rootFill: '#818cf8', secondFill: '#1e1b4b', lineColor: '#a5b4fc', bg: '#0f0d24' },
    config: buildBaseTheme({
      lineColor: '#818cf8',
      bg: '#1e1b4b',
      root: { fillColor: '#6366f1', color: '#ffffff' },
      second: { fillColor: '#312e81', color: '#e0e7ff', borderColor: '#6366f1' },
      node: { color: '#c7d2fe' },
      generalization: { fillColor: '#312e81', color: '#e0e7ff', borderColor: '#818cf8' }
    })
  }
]

/** 取配色预设（找不到时回退到第一套） */
export function getThemePreset(id: string | null | undefined): ThemePreset {
  return THEMES.find((t) => t.id === id) ?? THEMES[0]
}

export interface ResolveThemeOptions {
  /** 模板的完整配置，优先级高于预设主题 */
  templateConfig?: MindMapThemeConfig | null
  /** 页面外壳深色底，用于让画布与外壳同族（默认与 _theme.scss 的 .dark 一致） */
  shellBg?: string
  shellCardBg?: string
}

/**
 * 配色解析的唯一出口：色相（预设 / 模板）× 明暗（isDark）→ 可直接应用的完整配置。
 * 返回值始终是深拷贝，调用方可以安全地改动。
 */
export function resolveThemeConfig(
  themeId: string | null | undefined,
  isDark: boolean,
  options: ResolveThemeOptions = {}
): MindMapThemeConfig {
  const base = JSON.parse(
    JSON.stringify(options.templateConfig ?? getThemePreset(themeId).config)
  ) as MindMapThemeConfig
  if (!isDark) return base
  return deriveDarkConfig(base, { bg: options.shellBg, cardBg: options.shellCardBg })
}

export function getThemeIdOrDefault(id: string | null | undefined): string {
  if (!id) return 'classic'
  return THEMES.some((t) => t.id === id) ? id : 'classic'
}

/**
 * 获取导图主题的封面缩略图配色。
 * - isDark=true 时返回深色模式配色，与深色界面背景协调
 * - 根节点文字始终为白色，二级节点文字在深色模式下取 lineColor
 */
export function getThemeSwatch(
  themeId: string | null | undefined,
  isDark: boolean
): { rootFill: string; secondFill: string; lineColor: string; bg: string; secondTextColor: string } {
  const id = getThemeIdOrDefault(themeId)
  const theme = THEMES.find((t) => t.id === id) ?? THEMES[0]
  const swatch = isDark ? theme.swatchDark : theme.swatch
  return {
    ...swatch,
    // 二级节点文字颜色：浅色模式用根节点填充色（深），深色模式用 lineColor（浅）
    secondTextColor: isDark ? swatch.lineColor : swatch.rootFill
  }
}
