import type { Finding } from "./types";

export interface FilterState {
  symbolKinds: Set<string>;
  projects: Set<string>;
  minConfidence: number;
  searchText: string;
  searchRegex?: string;
}

export class FindingFilter {
  private state: FilterState = {
    symbolKinds: new Set(),
    projects: new Set(),
    minConfidence: 0,
    searchText: "",
    searchRegex: undefined,
  };
  private ignoredFindings: Set<string> = new Set();

  addIgnored(finding: Finding): void {
    this.ignoredFindings.add(this.getFindingId(finding));
  }

  removeIgnored(finding: Finding): void {
    this.ignoredFindings.delete(this.getFindingId(finding));
  }

  isIgnored(finding: Finding): boolean {
    return this.ignoredFindings.has(this.getFindingId(finding));
  }

  getIgnoredFindings(): Set<string> {
    return new Set(this.ignoredFindings);
  }

  setIgnoredFindings(ids: Set<string>): void {
    this.ignoredFindings = new Set(ids);
  }

  clearIgnored(): void {
    this.ignoredFindings.clear();
  }

  private getFindingId(finding: Finding): string {
    return `${finding.FilePath}:${finding.Line}:${finding.SymbolName}`;
  }

  setSymbolKindFilter(kinds: string[]): void {
    this.state.symbolKinds = new Set(kinds);
  }

  setProjectFilter(projects: string[]): void {
    this.state.projects = new Set(projects);
  }

  setConfidenceFilter(minConfidence: number): void {
    this.state.minConfidence = minConfidence;
  }

  setSearchText(text: string): void {
    this.state.searchText = text.toLowerCase();
    this.state.searchRegex = undefined;
  }

  setSearchRegex(pattern: string): void {
    this.state.searchRegex = pattern;
    this.state.searchText = "";
  }

  clearAll(): void {
    this.state = {
      symbolKinds: new Set(),
      projects: new Set(),
      minConfidence: 0,
      searchText: "",
      searchRegex: undefined,
    };
  }

  matches(finding: Finding): boolean {
    // Check ignored first
    if (this.ignoredFindings.size > 0 && this.isIgnored(finding)) {
      return false;
    }

    // Symbol kind filter
    if (
      this.state.symbolKinds.size > 0 &&
      !this.state.symbolKinds.has(finding.SymbolKind)
    ) {
      return false;
    }

    // Project filter
    if (
      this.state.projects.size > 0 &&
      !this.state.projects.has(finding.Project)
    ) {
      return false;
    }

    // Confidence filter: treat missing confidence as 0 so the filter is effective
    if (this.state.minConfidence > 0) {
      const confidence = finding.confidence ?? 0;
      if (confidence < this.state.minConfidence) {
        return false;
      }
    }

    // Search text filter
    if (this.state.searchText) {
      const searchableText = [
        finding.SymbolName ?? "",
        finding.ContainingType ?? "",
        finding.Project ?? "",
        finding.FilePath ?? "",
        finding.Remarks ?? "",
      ]
        .join(" ")
        .toLowerCase();

      if (!searchableText.includes(this.state.searchText)) {
        return false;
      }
    }

    // Search regex filter
    if (this.state.searchRegex) {
      try {
        const regex = new RegExp(this.state.searchRegex, "i");
        const searchableText = [
          finding.SymbolName ?? "",
          finding.ContainingType ?? "",
          finding.Project ?? "",
          finding.FilePath ?? "",
          finding.Remarks ?? "",
        ].join(" ");

        if (!regex.test(searchableText)) {
          return false;
        }
      } catch (e) {
        return false;
      }
    }

    return true;
  }

  hasActiveFilters(): boolean {
    return (
      this.state.symbolKinds.size > 0 ||
      this.state.projects.size > 0 ||
      this.state.minConfidence > 0 ||
      this.state.searchText !== "" ||
      this.state.searchRegex !== undefined ||
      this.ignoredFindings.size > 0
    );
  }

  getState(): FilterState {
    return {
      ...this.state,
      symbolKinds: new Set(this.state.symbolKinds),
      projects: new Set(this.state.projects),
    };
  }
}
