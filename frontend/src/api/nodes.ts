import { http } from './http'

/** 后端枚举 NodeShape：0=Rectangle 1=Rounded 2=Circle 3=Ellipse 4=Diamond 5=Parallelogram 6=Underline */
export type NodeShape = 0 | 1 | 2 | 3 | 4 | 5 | 6

/** 后端枚举 EdgeStyle：0=Solid 1=Dashed 2=Dotted 3=Curve */
export type EdgeStyle = 0 | 1 | 2 | 3

/** 后端枚举 Direction：0=Left 1=Right */
export type Direction = 0 | 1 | null

export interface NodeDto {
  id: string
  mindMapId: string
  parentId?: string | null
  title: string
  content?: string | null
  note?: string | null
  sortOrder: number
  isCollapsed: boolean
  x?: number | null
  y?: number | null
  width?: number | null
  height?: number | null
  color?: string | null
  fontSize?: number | null
  fontFamily?: string | null
  shape?: NodeShape | null
  icon?: string | null
  borderColor?: string | null
  backgroundColor?: string | null
  edgeColor?: string | null
  edgeStyle?: EdgeStyle | null
  direction?: Direction
  extraData?: string | null
  createdAt: string
  updatedAt: string
}

export interface NodeTreeNodeDto extends NodeDto {
  children: NodeTreeNodeDto[]
}

export interface NodeCreatePayload {
  parentId?: string | null
  title: string
  content?: string
  note?: string
  sortOrder?: number
  isCollapsed?: boolean
  x?: number
  y?: number
  width?: number
  height?: number
  color?: string
  fontSize?: number
  fontFamily?: string
  shape?: NodeShape
  icon?: string
  borderColor?: string
  backgroundColor?: string
  edgeColor?: string
  edgeStyle?: EdgeStyle
  direction?: Direction
  extraData?: string
}

export interface NodeUpdatePayload {
  title?: string
  content?: string
  note?: string
  sortOrder?: number
  isCollapsed?: boolean
  x?: number
  y?: number
  width?: number
  height?: number
  color?: string
  fontSize?: number
  fontFamily?: string
  shape?: NodeShape
  icon?: string
  borderColor?: string
  backgroundColor?: string
  edgeColor?: string
  edgeStyle?: EdgeStyle
  direction?: Direction
  extraData?: string
}

export interface NodeMovePayload {
  parentId?: string | null
  sortOrder?: number
  direction?: Direction | null
}

export interface NodeBatchItem {
  id: string
  sortOrder?: number
  parentId?: string | null
  direction?: Direction | null
  x?: number
  y?: number
  isCollapsed?: boolean
}

export interface NodeBatchPayload {
  nodes: NodeBatchItem[]
}

export async function fetchNodes(mindMapId: string): Promise<NodeDto[]> {
  return (await http.get(`/mindmaps/${mindMapId}/nodes`)) as unknown as NodeDto[]
}

export async function fetchNodeTree(mindMapId: string): Promise<NodeTreeNodeDto[]> {
  return (await http.get(`/mindmaps/${mindMapId}/nodes/tree`)) as unknown as NodeTreeNodeDto[]
}

export async function fetchNode(mindMapId: string, nodeId: string): Promise<NodeDto> {
  return (await http.get(`/mindmaps/${mindMapId}/nodes/${nodeId}`)) as unknown as NodeDto
}

export async function createNode(
  mindMapId: string,
  payload: NodeCreatePayload
): Promise<NodeDto> {
  return (await http.post(`/mindmaps/${mindMapId}/nodes`, payload)) as unknown as NodeDto
}

export async function updateNode(
  mindMapId: string,
  nodeId: string,
  payload: NodeUpdatePayload
): Promise<NodeDto> {
  return (await http.put(`/mindmaps/${mindMapId}/nodes/${nodeId}`, payload)) as unknown as NodeDto
}

export async function moveNode(
  mindMapId: string,
  nodeId: string,
  payload: NodeMovePayload
): Promise<NodeDto> {
  return (await http.post(`/mindmaps/${mindMapId}/nodes/${nodeId}/move`, payload)) as unknown as NodeDto
}

export async function batchUpdateNodes(
  mindMapId: string,
  payload: NodeBatchPayload
): Promise<void> {
  await http.put(`/mindmaps/${mindMapId}/nodes/batch`, payload)
}

export async function deleteNode(mindMapId: string, nodeId: string): Promise<void> {
  await http.delete(`/mindmaps/${mindMapId}/nodes/${nodeId}`)
}
