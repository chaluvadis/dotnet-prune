import * as vscode from "vscode";
import * as fs from "node:fs";
import * as path from "node:path";
import * as crypto from "node:crypto";

export interface FileHashEntry {
  filePath: string;
  hash: string;
  lastAnalyzed: number;
}

export interface CacheData {
  version: string;
  analyzerVersion: string;
  lastAnalysis: number;
  files: Record<string, FileHashEntry>;
}

const CACHE_DIR = ".dotnetprune";
const CACHE_FILE = "cache.json";
const CACHE_VERSION = "1.0.0";

export class FileHashCache {
  private cachePath: string;
  private cacheData: CacheData;
  private workspaceRoot: string;

  constructor(workspaceRoot: string) {
    this.workspaceRoot = workspaceRoot;
    this.cachePath = path.join(workspaceRoot, CACHE_DIR, CACHE_FILE);
    this.cacheData = this.loadCache();
  }

  private getCacheDir(): string {
    return path.join(this.workspaceRoot, CACHE_DIR);
  }

  private loadCache(): CacheData {
    try {
      if (fs.existsSync(this.cachePath)) {
        const content = fs.readFileSync(this.cachePath, "utf-8");
        const data = JSON.parse(content) as CacheData;
        if (data.version === CACHE_VERSION) {
          return data;
        }
      }
    } catch (error) {
      // Cache corrupted, start fresh
    }
    return this.createEmptyCache();
  }

  private createEmptyCache(): CacheData {
    return {
      version: CACHE_VERSION,
      analyzerVersion: "",
      lastAnalysis: 0,
      files: {},
    };
  }

  private saveCache(): void {
    try {
      const cacheDir = this.getCacheDir();
      if (!fs.existsSync(cacheDir)) {
        fs.mkdirSync(cacheDir, { recursive: true });
      }
      fs.writeFileSync(this.cachePath, JSON.stringify(this.cacheData, null, 2), "utf-8");
    } catch (error) {
      console.error("Failed to save cache:", error);
    }
  }

  async computeFileHash(filePath: string): Promise<string> {
    return new Promise((resolve, reject) => {
      const hash = crypto.createHash("sha256");
      const stream = fs.createReadStream(filePath);
      stream.on("data", (data) => hash.update(data));
      stream.on("end", () => resolve(hash.digest("hex")));
      stream.on("error", reject);
    });
  }

  async computeContentHash(content: string): Promise<string> {
    return crypto.createHash("sha256").update(content).digest("hex");
  }

  async getChangedFiles(filePaths: string[], analyzerVersion: string): Promise<{ changed: string[]; deleted: string[]; new: string[] }> {
    const changed: string[] = [];
    const deleted: string[] = [];
    const newFiles: string[] = [];

    for (const filePath of filePaths) {
      if (!fs.existsSync(filePath)) {
        if (this.cacheData.files[filePath]) {
          deleted.push(filePath);
        }
        continue;
      }

      const currentHash = await this.computeFileHash(filePath);
      const cachedEntry = this.cacheData.files[filePath];

      if (!cachedEntry) {
        newFiles.push(filePath);
      } else if (cachedEntry.hash !== currentHash) {
        changed.push(filePath);
      }
    }

    // Check for deleted files
    for (const cachedPath of Object.keys(this.cacheData.files)) {
      if (!fs.existsSync(cachedPath)) {
        deleted.push(cachedPath);
      }
    }

    return { changed, deleted, new: newFiles };
  }

  async updateCache(
    filePaths: string[],
    analyzerVersion: string,
    force: boolean = false
  ): Promise<void> {
    if (force) {
      this.cacheData = this.createEmptyCache();
    }

    this.cacheData.analyzerVersion = analyzerVersion;
    this.cacheData.lastAnalysis = Date.now();

    for (const filePath of filePaths) {
      try {
        if (fs.existsSync(filePath)) {
          const hash = await this.computeFileHash(filePath);
          this.cacheData.files[filePath] = {
            filePath,
            hash,
            lastAnalyzed: Date.now(),
          };
        } else if (this.cacheData.files[filePath]) {
          delete this.cacheData.files[filePath];
        }
      } catch (error) {
        console.error(`Failed to hash file ${filePath}:`, error);
      }
    }

    this.saveCache();
  }

  clearCache(): void {
    this.cacheData = this.createEmptyCache();
    this.saveCache();
  }

  getCacheSize(): number {
    try {
      if (fs.existsSync(this.cachePath)) {
        const stats = fs.statSync(this.cachePath);
        return stats.size;
      }
    } catch (error) {
      // Ignore
    }
    return 0;
  }

  hasValidCache(): boolean {
    return Object.keys(this.cacheData.files).length > 0;
  }

  getLastAnalysisTime(): number {
    return this.cacheData.lastAnalysis;
  }

  isCacheValid(analyzerVersion: string): boolean {
    return (
      this.cacheData.analyzerVersion === analyzerVersion &&
      this.cacheData.version === CACHE_VERSION &&
      this.hasValidCache()
    );
  }

  getCachedFiles(): string[] {
    return Object.keys(this.cacheData.files);
  }
}

let globalCache: FileHashCache | undefined;

export function getOrCreateCache(workspaceRoot: string): FileHashCache {
  if (!globalCache || globalCache["workspaceRoot"] !== workspaceRoot) {
    globalCache = new FileHashCache(workspaceRoot);
  }
  return globalCache;
}

export function clearGlobalCache(): void {
  globalCache = undefined;
}