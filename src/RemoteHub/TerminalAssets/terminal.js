"use strict";
const bridge = message => window.chrome.webview.postMessage(message);
const terminal = new Terminal({
  fontFamily: '"Cascadia Mono", Consolas, monospace',
  fontSize: 14,
  lineHeight: 1.2,
  cursorBlink: true,
  disableStdin: true,
  scrollback: 5000,
  screenReaderMode: true,
  allowProposedApi: false,
  theme: { background: getComputedStyle(document.documentElement).getPropertyValue("--terminal-background").trim() }
});
const fit = new FitAddon.FitAddon();
terminal.loadAddon(fit);
terminal.open(document.getElementById("terminal"));
let pending = "";
let inputBusy = false;
let started = false;
let stopped = false;
function sendInput() {
  if (inputBusy || !pending || !started || stopped) return;
  inputBusy = true;
  const data = pending;
  pending = "";
  bridge({ type: "input", data });
}
terminal.onData(data => {
  if (!started || stopped) return;
  if (pending.length + data.length > 1048576) {
    bridge({ type: "error", message: "Too much pending input. Wait for the SSH process before typing again." });
    return;
  }
  pending += data;
  sendInput();
});
terminal.attachCustomKeyEventHandler(event => {
  if (event.type === "keydown" && event.ctrlKey && event.shiftKey && ["C", "V"].includes(event.key.toUpperCase())) {
    event.preventDefault();
    bridge({ type: event.key.toUpperCase() === "C" ? "copy" : "paste-request" });
    return false;
  }
  return true;
});
document.addEventListener("paste", event => {
  event.preventDefault();
  event.stopImmediatePropagation();
  bridge({ type: "paste", data: event.clipboardData.getData("text/plain") });
}, true);
function resize() {
  if (document.body.clientWidth < 40 || document.body.clientHeight < 40) return;
  fit.fit();
  bridge({ type: "resize", columns: terminal.cols, rows: terminal.rows });
}
new ResizeObserver(resize).observe(document.body);
window.chrome.webview.addEventListener("message", event => {
  const message = event.data;
  if (message.type === "theme") {
    terminal.options.theme = message.colors;
    document.documentElement.style.setProperty("--terminal-background", message.colors.background);
    document.documentElement.style.setProperty("--terminal-scrollbar", message.colors.foreground);
    document.documentElement.style.colorScheme = message.dark ? "dark" : "light";
  }
  if (message.type === "opened") {
    started = true;
    terminal.options.disableStdin = false;
  }
  if (message.type === "output") terminal.write(message.data, () => bridge({ type: "ack", id: message.id }));
  if (message.type === "input-ack") { inputBusy = false; sendInput(); }
  if (message.type === "closed") {
    stopped = true;
    pending = "";
    terminal.options.disableStdin = true;
    terminal.options.cursorBlink = false;
  }
  if (message.type === "focus") terminal.focus();
});
window.terminalSelection = () => terminal.getSelection();
window.terminalTheme = () => terminal.options.theme;
window.terminalPaste = text => { if (started && !stopped) terminal.paste(text); };
window.terminalText = () => Array.from({ length: terminal.buffer.active.length },
  (_, index) => terminal.buffer.active.getLine(index).translateToString(true)).join("\n");
resize();
bridge({ type: "ready", columns: terminal.cols, rows: terminal.rows });
