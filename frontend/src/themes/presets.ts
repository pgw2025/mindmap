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
  config: MindMapThemeConfig
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

export function getThemeConfig(id: string): MindMapThemeConfig {
  const preset = THEMES.find((t) => t.id === id)
  return preset ? preset.config : THEMES[0].config
}

export function getThemeIdOrDefault(id: string | null | undefined): string {
  if (!id) return 'classic'
  return THEMES.some((t) => t.id === id) ? id : 'classic'
}
