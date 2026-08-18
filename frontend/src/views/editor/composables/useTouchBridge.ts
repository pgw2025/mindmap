import type { Ref } from 'vue'
import type MindMap from 'simple-mind-map'

/**
 * 触摸事件 → 鼠标事件桥接 + 双指捏合缩放
 * simple-mind-map 默认只绑定 mouse 事件，移动端需要手动桥接
 */
export function useTouchBridge(opts: {
  mindMapRef: Ref<HTMLElement | null>
  getMindMapInstance: () => MindMap | null
}) {
  const { mindMapRef, getMindMapInstance } = opts

  let touchBridgeBound = false

  /** 派发合成鼠标事件到指定 target */
  function dispatchMouse(type: string, touch: Touch, target: Element) {
    const evt = new MouseEvent(type, {
      bubbles: true,
      cancelable: true,
      view: window,
      detail: 1,
      clientX: touch.clientX,
      clientY: touch.clientY,
      button: 0,
      buttons: type === 'mouseup' ? 0 : 1
    })
    ;(evt as any)._fromTouch = true
    target.dispatchEvent(evt)
  }

  /** 元素 + 指定坐标下的实际目标 */
  function elemAtPoint(el: Element, x: number, y: number): Element {
    const hit = document.elementFromPoint(x, y)
    if (hit && (el === hit || el.contains(hit))) return hit
    return el
  }

  /** 两指距离 */
  function distance(t1: Touch, t2: Touch) {
    const dx = t1.clientX - t2.clientX
    const dy = t1.clientY - t2.clientY
    return Math.hypot(dx, dy)
  }

  /** 两指中心点（相对于容器） */
  function pinchCenter(t1: Touch, t2: Touch) {
    const rect = el!.getBoundingClientRect()
    return {
      cx: (t1.clientX + t2.clientX) / 2 - rect.left,
      cy: (t1.clientY + t2.clientY) / 2 - rect.top
    }
  }

  let el: HTMLElement | null = null

  function bindTouchBridge() {
    el = mindMapRef.value
    if (!el || touchBridgeBound) return
    touchBridgeBound = true

    // —— 单指拖拽状态 ——
    let downTarget: Element | null = null
    let lastTouch: Touch | null = null
    let moved = false

    // —— 双指捏合状态 ——
    let pinching = false
    let pinchStartDist = 0
    let pinchStartScale = 1
    let pinchCenterPoint = { cx: 0, cy: 0 }

    el.addEventListener('touchstart', (e: TouchEvent) => {
      if (e.touches.length >= 2) {
        // 进入捏合模式，重置单指状态
        pinching = true
        downTarget = null
        lastTouch = null
        moved = false
        const t1 = e.touches[0]
        const t2 = e.touches[1]
        pinchStartDist = distance(t1, t2)
        pinchStartScale = (getMindMapInstance()?.view as any)?.scale ?? 1
        pinchCenterPoint = pinchCenter(t1, t2)
        e.preventDefault()
        return
      }
      if (pinching) {
        // 之前是捏合，现在只剩一指：退出捏合模式
        pinching = false
      }
      const t = e.touches[0]
      lastTouch = t
      moved = false
      downTarget = elemAtPoint(el!, t.clientX, t.clientY)
      dispatchMouse('mousedown', t, downTarget)
    }, { passive: false, capture: false })

    el.addEventListener('touchmove', (e: TouchEvent) => {
      if (pinching && e.touches.length >= 2) {
        // 双指捏合缩放
        const t1 = e.touches[0]
        const t2 = e.touches[1]
        const curDist = distance(t1, t2)
        const view: any = getMindMapInstance()?.view
        if (pinchStartDist > 0 && view) {
          const ratio = curDist / pinchStartDist
          const targetScale = Math.max(0.2, Math.min(4, pinchStartScale * ratio))
          view.setScale(targetScale, pinchCenterPoint.cx, pinchCenterPoint.cy)
        }
        e.preventDefault()
        return
      }
      if (e.touches.length > 1 || !lastTouch || !downTarget) return
      const t = e.touches[0]
      if (moved || Math.abs(t.clientX - lastTouch.clientX) > 4 || Math.abs(t.clientY - lastTouch.clientY) > 4) {
        moved = true
        e.preventDefault()
      }
      lastTouch = t
      dispatchMouse('mousemove', t, downTarget)
    }, { passive: false, capture: false })

    el.addEventListener('touchend', (e: TouchEvent) => {
      if (e.touches.length >= 2) {
        // 还在捏合，保持状态
        return
      }
      if (pinching) {
        // 捏合结束，可能还有一指残留
        pinching = false
        if (e.touches.length === 1) {
          // 只剩一指：作为新的单指起点
          const t = e.touches[0]
          lastTouch = t
          moved = false
          downTarget = elemAtPoint(el!, t.clientX, t.clientY)
          dispatchMouse('mousedown', t, downTarget)
        }
        return
      }
      if (!lastTouch || !downTarget) {
        downTarget = null
        lastTouch = null
        return
      }
      dispatchMouse('mouseup', lastTouch, downTarget)
      downTarget = null
      lastTouch = null
      moved = false
    }, { passive: true, capture: false })

    el.addEventListener('touchcancel', () => {
      if (lastTouch && downTarget && !pinching) {
        dispatchMouse('mouseup', lastTouch, downTarget)
      }
      downTarget = null
      lastTouch = null
      moved = false
      pinching = false
    }, { passive: true })
  }

  return { bindTouchBridge }
}
