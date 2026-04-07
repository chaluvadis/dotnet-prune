import * as vscode from "vscode";

export type LogLevel = "debug" | "info" | "warn" | "error";

export class Logger {
  private channel: vscode.OutputChannel;

  constructor(name: string = "DotNetPrune") {
    this.channel = vscode.window.createOutputChannel(name);
  }

  log(level: LogLevel, message: string): void {
    const timestamp = new Date().toISOString().split("T")[1].split(".")[0];
    const prefixed = `[${timestamp}] [${level.toUpperCase()}] ${message}`;
    this.channel.appendLine(prefixed);
    
    if (level === "error" || level === "warn") {
      this.channel.show(true);
    }
  }

  debug(message: string): void {
    this.log("debug", message);
  }

  info(message: string): void {
    this.log("info", message);
  }

  warn(message: string): void {
    this.log("warn", message);
  }

  error(message: string): void {
    this.log("error", message);
  }

  show(): void {
    this.channel.show(true);
  }

  clear(): void {
    this.channel.clear();
  }

  dispose(): void {
    this.channel.dispose();
  }
}

export const logger = new Logger("DotNetPrune");
