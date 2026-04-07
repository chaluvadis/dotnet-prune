import * as fs from "node:fs";
import * as path from "node:path";

export class PathResolver {
  private workspaceRoot: string;
  private solutionProjectMap: Map<string, string> = new Map();
  private projectCache: Map<string, string | null> = new Map();

  constructor(workspaceRoot: string) {
    this.workspaceRoot = workspaceRoot;
  }

  setWorkspaceRoot(root: string): void {
    this.workspaceRoot = root;
    this.clearCache();
  }

  clearCache(): void {
    this.projectCache.clear();
  }

  getProjectForFile(filePath: string): string | null {
    if (this.projectCache.has(filePath)) {
      return this.projectCache.get(filePath) ?? null;
    }

    const result = this.findProjectForFile(filePath);
    this.projectCache.set(filePath, result ?? "NO_PROJECT");
    return result;
  }

  private findProjectForFile(filePath: string): string | null {
    try {
      const fileDir = path.dirname(filePath);
      let currentDir = fileDir;

      while (currentDir !== this.workspaceRoot && currentDir !== path.dirname(currentDir)) {
        try {
          const files = fs.readdirSync(currentDir);
          const csprojFiles = files.filter(f => f.endsWith('.csproj'));

          if (csprojFiles.length > 0) {
            return path.basename(csprojFiles[0], '.csproj');
          }
        } catch {
          // Continue if we can't read the directory
        }
        currentDir = path.dirname(currentDir);
      }
    } catch {
      // Ignore errors
    }
    return null;
  }

  getSolutionForFile(filePath: string): string | null {
    const projectName = this.getProjectForFile(filePath);
    if (projectName) {
      return this.solutionProjectMap.get(projectName) ?? null;
    }
    return this.findSolutionByFilePath(filePath);
  }

  getProjectNameFromPath(filePath: string): string {
    try {
      const relativePath = path.relative(this.workspaceRoot, filePath);
      const parts = relativePath.split(path.sep);

      if (parts.length > 1) {
        const skipDirectories = ['src', 'lib', 'test', 'tests', 'assets', 'resources', 'common', 'models', 'services', 'controllers'];
        
        for (let i = 0; i < parts.length - 1; i++) {
          const part = parts[i];
          if (skipDirectories.includes(part.toLowerCase())) {
            continue;
          }
          
          if (part && !part.includes('.')) {
            const projectFolderPath = path.join(this.workspaceRoot, parts.slice(0, i + 1).join(path.sep));
            if (fs.existsSync(projectFolderPath)) {
              try {
                const files = fs.readdirSync(projectFolderPath);
                const hasCsFiles = files.some(f => f.endsWith('.cs'));
                const hasCsproj = files.some(f => f.endsWith('.csproj'));
                if (hasCsFiles || hasCsproj) {
                  return part;
                }
              } catch {
                // Continue
              }
            }
          }
        }
      }

      const projectInfo = this.getProjectForFile(filePath);
      if (projectInfo) {
        return projectInfo;
      }

      const topLevelDir = parts[0];
      if (topLevelDir) {
        return topLevelDir;
      }

      return "Project";
    } catch {
      return "Project";
    }
  }

  getSolutionNameFromPath(filePath: string): string {
    try {
      const relativePath = path.relative(this.workspaceRoot, filePath);
      const parts = relativePath.split(path.sep);

      for (let i = 0; i < parts.length; i++) {
        const currentPath = parts.slice(0, i + 1).join(path.sep);
        const dirPath = path.join(this.workspaceRoot, currentPath);

        if (fs.existsSync(dirPath)) {
          const files = fs.readdirSync(dirPath);
          const hasSlnFile = files.some(f => f.toLowerCase().endsWith('.sln') || f.toLowerCase().endsWith('.slnx'));
          if (hasSlnFile) {
            return path.basename(currentPath);
          }
        }
      }

      return path.basename(this.workspaceRoot);
    } catch {
      return path.basename(this.workspaceRoot);
    }
  }

  private findSolutionByFilePath(filePath: string): string | null {
    try {
      const fileDir = path.dirname(filePath);
      let currentDir = fileDir;

      while (currentDir !== this.workspaceRoot && currentDir !== path.dirname(currentDir)) {
        try {
          const files = fs.readdirSync(currentDir);
          const slnFile = files.find(f => f.toLowerCase().endsWith('.sln') || f.toLowerCase().endsWith('.slnx'));
          if (slnFile) {
            return path.basename(slnFile, path.extname(slnFile));
          }
        } catch {
          // Continue
        }
        currentDir = path.dirname(currentDir);
      }
    } catch {
      // Ignore
    }
    return null;
  }

  buildSolutionProjectMap(solutions: Array<{path: string; projects: string[]}>): void {
    this.solutionProjectMap.clear();
    for (const sol of solutions) {
      const solName = path.basename(sol.path, path.extname(sol.path));
      for (const proj of sol.projects) {
        const projName = path.basename(proj, '.csproj');
        this.solutionProjectMap.set(projName, solName);
      }
    }
  }
}

let globalResolver: PathResolver | null = null;

export function getOrCreatePathResolver(workspaceRoot: string): PathResolver {
  if (!globalResolver) {
    globalResolver = new PathResolver(workspaceRoot);
  } else {
    globalResolver.setWorkspaceRoot(workspaceRoot);
  }
  return globalResolver;
}

export function clearGlobalPathResolver(): void {
  globalResolver = null;
}
