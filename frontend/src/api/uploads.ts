import { http } from './http'

/** 后端节点图片上传结果 */
export interface NodeImageUploadResult {
  url: string
  width: number
  height: number
}

/** 后端限制：图片 ≤ 5MB（与 UploadsController.MaxImageSizeBytes 一致） */
export const MAX_IMAGE_SIZE_BYTES = 5 * 1024 * 1024

/** 用浏览器读取图片真实宽高（后端尺寸探测失败时兜底） */
export function readImageSize(src: string): Promise<{ width: number; height: number }> {
  return new Promise((resolve) => {
    const img = new Image()
    img.onload = () => resolve({ width: img.naturalWidth, height: img.naturalHeight })
    img.onerror = () => resolve({ width: 0, height: 0 })
    img.src = src
  })
}

/**
 * 上传节点图片。返回站内相对 URL 与原始宽高（后端按文件头探测，失败时宽高为 0，
 * 前端应再用 Image.onload 兜底读取真实尺寸）。
 */
export async function uploadNodeImage(file: File): Promise<NodeImageUploadResult> {
  const formData = new FormData()
  formData.append('file', file)
  return (await http.post('/uploads/node-image', formData)) as unknown as NodeImageUploadResult
}
