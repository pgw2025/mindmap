declare module 'simple-mind-map/src/plugins/Search.js' {
  const Search: { instanceName: string }
  export default Search
}

declare module 'simple-mind-map/src/plugins/Export.js' {
  const Export: { instanceName: string }
  export default Export
}

declare module 'simple-mind-map/src/plugins/ExportPDF.js' {
  const ExportPDF: { instanceName: string }
  export default ExportPDF
}

declare module 'simple-mind-map/src/plugins/ExportXMind.js' {
  const ExportXMind: { instanceName: string }
  export default ExportXMind
}

declare module 'simple-mind-map/src/plugins/Drag.js' {
  const Drag: { instanceName: string; prototype: any }
  export default Drag
}

declare module 'simple-mind-map/src/plugins/Select.js' {
  const Select: { instanceName: string; prototype: any }
  export default Select
}

declare module 'simple-mind-map/src/plugins/TouchEvent.js' {
  const TouchEvent: { instanceName: string; prototype: any }
  export default TouchEvent
}

declare module 'simple-mind-map' {
  export default class MindMap {
    static readonly TREE: string
    static readonly MIND: string
    static readonly FISHBONE: string
    static readonly TIMELINE: string
    static usePlugin(plugin: unknown, opt?: unknown): void

    view?: {
      enlarge(): void
      narrow(): void
      reset(): void
      fit(): void
    }

    search?: {
      search(text: string, callback?: () => void): void
      searchNext(callback?: () => void): void
      searchPrev(callback?: () => void): void
      endSearch(): void
    }

    doExport?: {
      export(type: string, isDownload?: boolean, name?: string, ...args: unknown[]): Promise<unknown>
    }

    renderer?: any
    draggable?: any
    draw?: any

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
      beforeDragEnd?: unknown
      [key: string]: any
    })

    setThemeConfig(config: any, notRender?: boolean, ...args: any[]): void
    setTheme(theme: string): void
    setData(data: unknown): void
    getData(): unknown
    execCommand(name: string, ...args: unknown[]): void
    focusNode(nodeId: string): void
    destroy(): void
    on(event: string, handler: (...args: unknown[]) => void): void
    off(event: string, handler?: (...args: unknown[]) => void): void
    [key: string]: any
  }
}
