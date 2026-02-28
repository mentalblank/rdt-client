export interface UsenetJob {
  usenetJobId: string;
  hash: string;
  category: string;
  jobName: string;
  nzbFileName: string;
  totalSize: number;
  added: Date;
  completed?: Date;
  priority: number;
  error: string;
  status: number;
  fileCount: number;
  files: UsenetFile[];
}

export interface UsenetFile {
  usenetFileId: string;
  usenetJobId: string;
  path: string;
  size: number;
}
