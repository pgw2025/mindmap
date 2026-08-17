declare module 'simple-mind-map' {
  export default class MindMap {
    static readonly TREE: string
    static readonly MIND: string
    static readonly FISHBONE: string
    static readonly TIMELINE: string

    view?: {
      enlarge(): void
      narrow(): void
      reset(): void
      fit(): void
    }

    constructor(opt?: {
      el?: HTMLElement
      data?: unknown
      theme?: string
      layout?: string
      draggable?: boolean
      contextMenu?: boolean
      toolBar?: boolean
      nodeLineDash?: boolean
      enableFreeDrag?: boolean
      scrollbarStyle?: string
      minScale?: number
      maxScale?: number
    })

    setData(data: unknown): void
    getData(): unknown
    execCommand(name: string, ...args: unknown[]): void
    focusNode(nodeId: string): void
    destroy(): void
    on(event: string, handler: (...args: unknown[]) => void): void
    off(event: string, handler?: (...args: unknown[]) => void): void
  }
}
