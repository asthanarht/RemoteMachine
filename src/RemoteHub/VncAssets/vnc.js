import RFB from "./novnc/core/rfb.js";
import { initLogging } from "./novnc/core/util/logging.js";
initLogging("none");
const post = value => window.chrome.webview.postMessage(value);
let rfb, channel, ended = false, credentialsPending = false;
let receiveBuffer, receiveSlotSize = 0, receiveSlots = 0, receivedSequence = 0;
let fitQueued = false;
function fitDesktop() {
    if (fitQueued) return;
    fitQueued = true;
    requestAnimationFrame(() => {
        fitQueued = false;
        if (rfb && !ended && innerWidth > 0 && innerHeight > 0) rfb.scaleViewport = true;
    });
}
function fail() {
    if (ended) return;
    ended = true;
    post({ type: "error" });
    rfb?.disconnect();
}
class NativeChannel {
    constructor() {
        this.readyState = "open";
        this.protocol = "";
        this.binaryType = "arraybuffer";
        this.onopen = this.onclose = this.onerror = this.onmessage = null;
        this.queue = [];
        this.pending = false;
        this.bufferedAmount = 0;
        this.drained = [];
    }
    send(data) {
        if (this.readyState !== "open") throw new Error("VNC transport is closed");
        const bytes = new Uint8Array(data);
        this.bufferedAmount += bytes.length;
        if (this.bufferedAmount > 1048576) { fail(); return; }
        this.queue.push(bytes.slice());
        this.flush();
    }
    get onmessage() { return this._onmessage; }
    set onmessage(handler) {
        this._onmessage = handler;
        if (handler && !this.attached) {
            this.attached = true;
            queueMicrotask(() => post({ type: "transport-ready" }));
        }
    }
    flush() {
        if (this.pending || this.queue.length === 0 || this.readyState !== "open") return;
        const bytes = this.queue.shift();
        this.pending = true;
        this.inflight = bytes.length;
        post({ type: "send", data: btoa(String.fromCharCode(...bytes)) });
    }
    ack() {
        this.bufferedAmount -= this.inflight; this.pending = false; this.flush();
        if (!this.pending && this.queue.length === 0) {
            for (const callback of this.drained.splice(0)) callback();
        }
    }
    afterSend(callback) {
        if (this.pending || this.queue.length) this.drained.push(callback);
        else callback();
    }
    receive(bytes) {
        // noVNC copies incoming bytes into its receive queue synchronously, before the slot is acknowledged.
        this.onmessage?.({ data: bytes });
    }
    close() {
        if (this.readyState === "closed") return;
        this.readyState = "closed"; this.queue = []; this.drained = []; this.bufferedAmount = 0;
        this.onclose?.({ code: 1000, wasClean: true });
        post({ type: "closed" });
    }
}
window.chrome.webview.addEventListener("sharedbufferreceived", event => {
    try {
        if (receiveBuffer) throw new Error("Duplicate receive buffer");
        const buffer = event.getBuffer(), metadata = event.additionalData;
        if (metadata.slotSize !== 262144 || metadata.slots !== 4 || buffer.byteLength !== 1048576)
            throw new Error("Invalid receive buffer");
        receiveBuffer = buffer; receiveSlotSize = metadata.slotSize; receiveSlots = metadata.slots;
        post({ type: "buffer-ready" });
    } catch { fail(); }
});
window.chrome.webview.addEventListener("message", event => {
    try {
        const message = event.data;
        switch (message.type) {
            case "start":
                if (rfb) throw new Error("Duplicate start");
                channel = new NativeChannel();
                rfb = new RFB(document.getElementById("screen"), channel, { shared: true });
                rfb.scaleViewport = true;
                rfb.resizeSession = false;
                rfb.showDotCursor = true;
                rfb.viewOnly = message.viewOnly;
                rfb.wheelSensitivity = message.scrollSpeed;
                rfb.addEventListener("credentialsrequired", event => {
                    if (credentialsPending) return;
                    credentialsPending = true;
                    channel.afterSend(() => post({ type: "credentials", types: event.detail.types }));
                });
                rfb.addEventListener("connect", () => post({ type: "connected" }));
                rfb.addEventListener("disconnect", event => { if (!ended) { ended = true; post({ type: "disconnected", clean: event.detail.clean }); } });
                rfb.addEventListener("securityfailure", () => post({ type: "security-failure" }));
                rfb.addEventListener("serververification", fail);
                new MutationObserver(() => {
                    const canvas = document.querySelector("canvas");
                    if (canvas && (canvas.width > 16384 || canvas.height > 16384 || canvas.width * canvas.height > 33554432)) fail();
                }).observe(document.getElementById("screen"), { attributes: true, subtree: true, attributeFilter: ["width", "height"] });
                break;
            case "data":
                if (!receiveBuffer || message.id !== receivedSequence + 1 ||
                    message.offset !== (receivedSequence % receiveSlots) * receiveSlotSize ||
                    !Number.isInteger(message.count) || message.count < 1 || message.count > receiveSlotSize)
                    throw new Error("Invalid receive slot");
                channel.receive(new Uint8Array(receiveBuffer, message.offset, message.count));
                receivedSequence++;
                post({ type: "ack", id: message.id });
                break;
            case "send-ack": channel.ack(); break;
            case "credentials": credentialsPending = false; rfb.sendCredentials(message.credentials); break;
            case "focus": rfb.focus(); break;
            case "fit": fitDesktop(); break;
            case "scroll-speed": rfb.wheelSensitivity = message.value; break;
            case "blur": rfb.blur(); break;
            case "shortcut":
                if (rfb.viewOnly) break;
                const keys = { copy: [0x63, "KeyC"], paste: [0x76, "KeyV"], spotlight: [0x20, "Space"], switch: [0xff09, "Tab"] };
                const key = keys[message.action];
                if (!key) throw new Error("Invalid shortcut");
                rfb.sendKey(0xffe7, "MetaLeft", true);
                rfb.sendKey(key[0], key[1]);
                rfb.sendKey(0xffe7, "MetaLeft", false);
                break;
            case "close": rfb?.disconnect(); break;
            default: throw new Error("Invalid bridge message");
        }
    } catch { fail(); }
});
window.addEventListener("blur", () => rfb?.blur());
window.addEventListener("pagehide", () => { if (receiveBuffer) { window.chrome.webview.releaseBuffer(receiveBuffer); receiveBuffer = null; } });
window.addEventListener("error", fail);
window.addEventListener("unhandledrejection", fail);
window.vncSnapshot = () => {
    if (!rfb) return null;
    const image = rfb.getImageData();
    return { width: image.width, height: image.height, firstPixel: Array.from(image.data.slice(0, 4)), lastPixel: Array.from(image.data.slice(-4)) };
};
window.vncLayout = () => {
    const canvas = document.querySelector("canvas"), bounds = canvas.getBoundingClientRect();
    return { viewportWidth: innerWidth, viewportHeight: innerHeight, x: bounds.x, y: bounds.y,
        width: bounds.width, height: bounds.height, remoteWidth: canvas.width, remoteHeight: canvas.height };
};
window.vncValidatePattern = frame => {
    const { data } = rfb.getImageData();
    for (let i = 0; i < data.length; i += 4)
        if (data[i] !== (i / 4 + frame) % 251 || data[i + 1] !== 32 || data[i + 2] !== 50 || data[i + 3] !== 255) return false;
    return true;
};
window.vncTestKey = () => rfb.sendKey(0x61, "KeyA");
window.vncTestPointer = () => {
    const canvas = document.querySelector("canvas"), bounds = canvas.getBoundingClientRect();
    for (const type of ["mousedown", "mouseup"])
        canvas.dispatchEvent(new MouseEvent(type, { clientX: bounds.left + bounds.width / 2, clientY: bounds.top + bounds.height / 2, button: 0, buttons: type === "mousedown" ? 1 : 0, bubbles: true }));
};
window.vncTestWheel = events => {
    const canvas = document.querySelector("canvas"), bounds = canvas.getBoundingClientRect();
    for (const event of events)
        canvas.dispatchEvent(new WheelEvent("wheel", { ...event, clientX: bounds.left + bounds.width / 2,
            clientY: bounds.top + bounds.height / 2, bubbles: true, cancelable: true }));
    rfb.sendKey(0x62, "KeyB");
};
post({ type: "ready" });
