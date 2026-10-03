// Minimal Node Buffer and CommonJS compatibility layer for Ice-Hail's writer.
// PhysicsTool supplies loadIceHailModule() from managed embedded resources.
(function (global) {
    class Buffer extends Uint8Array {
        static alloc(size) { return new Buffer(size); }
        static from(value, encoding) {
            if (typeof value === "string") {
                const bytes = [];
                for (let i = 0; i < value.length; i++) {
                    let code = value.charCodeAt(i);
                    if (code >= 0xd800 && code <= 0xdbff && i + 1 < value.length) {
                        const low = value.charCodeAt(++i);
                        code = 0x10000 + ((code & 0x3ff) << 10) + (low & 0x3ff);
                    }
                    if (code < 0x80) bytes.push(code);
                    else if (code < 0x800) bytes.push(0xc0 | (code >> 6), 0x80 | (code & 0x3f));
                    else if (code < 0x10000) bytes.push(0xe0 | (code >> 12), 0x80 | ((code >> 6) & 0x3f), 0x80 | (code & 0x3f));
                    else bytes.push(0xf0 | (code >> 18), 0x80 | ((code >> 12) & 0x3f), 0x80 | ((code >> 6) & 0x3f), 0x80 | (code & 0x3f));
                }
                return new Buffer(bytes);
            }
            return new Buffer(value);
        }
        static concat(values) {
            const length = values.reduce((sum, value) => sum + value.length, 0);
            const result = new Buffer(length);
            let offset = 0;
            for (const value of values) { result.set(value, offset); offset += value.length; }
            return result;
        }
        static compare(a, b) {
            const length = Math.min(a.length, b.length);
            for (let i = 0; i < length; i++) if (a[i] !== b[i]) return a[i] < b[i] ? -1 : 1;
            return a.length === b.length ? 0 : (a.length < b.length ? -1 : 1);
        }
        slice(start, end) { return new Buffer(super.slice(start, end)); }
        copy(target, targetStart, sourceStart, sourceEnd) {
            targetStart = targetStart || 0; sourceStart = sourceStart || 0;
            sourceEnd = sourceEnd == null ? this.length : sourceEnd;
            target.set(this.subarray(sourceStart, sourceEnd), targetStart);
            return Math.min(sourceEnd - sourceStart, target.length - targetStart);
        }
        toString(encoding, start, end) {
            start = start || 0; end = end == null ? this.length : end;
            let result = "";
            for (let i = start; i < end;) {
                const a = this[i++];
                if (a < 0x80) result += String.fromCharCode(a);
                else if ((a & 0xe0) === 0xc0) result += String.fromCharCode(((a & 0x1f) << 6) | (this[i++] & 0x3f));
                else if ((a & 0xf0) === 0xe0) result += String.fromCharCode(((a & 0x0f) << 12) | ((this[i++] & 0x3f) << 6) | (this[i++] & 0x3f));
                else {
                    let code = ((a & 7) << 18) | ((this[i++] & 0x3f) << 12) | ((this[i++] & 0x3f) << 6) | (this[i++] & 0x3f);
                    code -= 0x10000;
                    result += String.fromCharCode(0xd800 | (code >> 10), 0xdc00 | (code & 0x3ff));
                }
            }
            return result;
        }
        _view() { return new DataView(this.buffer, this.byteOffset, this.byteLength); }
        readUInt8(o) { return this[o]; }
        readInt8(o) { return this._view().getInt8(o); }
        readUInt16LE(o) { return this._view().getUint16(o, true); }
        readUInt16BE(o) { return this._view().getUint16(o, false); }
        readInt16LE(o) { return this._view().getInt16(o, true); }
        readInt16BE(o) { return this._view().getInt16(o, false); }
        readUInt32LE(o) { return this._view().getUint32(o, true); }
        readUInt32BE(o) { return this._view().getUint32(o, false); }
        readInt32LE(o) { return this._view().getInt32(o, true); }
        readInt32BE(o) { return this._view().getInt32(o, false); }
        readFloatLE(o) { return this._view().getFloat32(o, true); }
        readFloatBE(o) { return this._view().getFloat32(o, false); }
        readDoubleLE(o) { return this._view().getFloat64(o, true); }
        readDoubleBE(o) { return this._view().getFloat64(o, false); }
        writeUInt8(v, o) { this[o] = v; return o + 1; }
        writeInt8(v, o) { this._view().setInt8(o, v); return o + 1; }
        writeUInt16LE(v, o) { this._view().setUint16(o || 0, v, true); return (o || 0) + 2; }
        writeUInt16BE(v, o) { this._view().setUint16(o || 0, v, false); return (o || 0) + 2; }
        writeInt16LE(v, o) { this._view().setInt16(o || 0, v, true); return (o || 0) + 2; }
        writeInt16BE(v, o) { this._view().setInt16(o || 0, v, false); return (o || 0) + 2; }
        writeUInt32LE(v, o) { this._view().setUint32(o || 0, v, true); return (o || 0) + 4; }
        writeUInt32BE(v, o) { this._view().setUint32(o || 0, v, false); return (o || 0) + 4; }
        writeInt32LE(v, o) { this._view().setInt32(o || 0, v, true); return (o || 0) + 4; }
        writeInt32BE(v, o) { this._view().setInt32(o || 0, v, false); return (o || 0) + 4; }
        writeFloatLE(v, o) { this._view().setFloat32(o || 0, v, true); return (o || 0) + 4; }
        writeFloatBE(v, o) { this._view().setFloat32(o || 0, v, false); return (o || 0) + 4; }
        writeDoubleLE(v, o) { this._view().setFloat64(o || 0, v, true); return (o || 0) + 8; }
        writeDoubleBE(v, o) { this._view().setFloat64(o || 0, v, false); return (o || 0) + 8; }
    }

    const cache = Object.create(null);
    function normalize(path) {
        const parts = [];
        for (const part of path.replace(/\\/g, "/").split("/")) {
            if (!part || part === ".") continue;
            if (part === "..") parts.pop(); else parts.push(part);
        }
        return parts.join("/");
    }
    function resolve(request, parent) {
        if (request === "fs") return request;
        let path = request;
        if (request[0] === ".") path = (parent ? parent.substring(0, parent.lastIndexOf("/") + 1) : "") + request;
        path = normalize(path);
        if (!path.endsWith(".js")) path += ".js";
        return path;
    }
    function requireModule(request, parent) {
        const id = resolve(request, parent || "");
        if (id === "fs") return {};
        if (cache[id]) return cache[id].exports;
        const source = loadIceHailModule(id);
        if (source == null) throw new Error("Embedded Ice-Hail module not found: " + id);
        const module = { exports: {} };
        cache[id] = module;
        const localRequire = function (child) { return requireModule(child, id); };
        new Function("module", "exports", "require", source)(module, module.exports, localRequire);
        return module.exports;
    }
    function base64(bytes) {
        const alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
        let result = "";
        for (let i = 0; i < bytes.length; i += 3) {
            const a = bytes[i], b = i + 1 < bytes.length ? bytes[i + 1] : 0, c = i + 2 < bytes.length ? bytes[i + 2] : 0;
            result += alphabet[a >> 2] + alphabet[((a & 3) << 4) | (b >> 4)] +
                (i + 1 < bytes.length ? alphabet[((b & 15) << 2) | (c >> 6)] : "=") +
                (i + 2 < bytes.length ? alphabet[c & 63] : "=");
        }
        return result;
    }

    global.Buffer = Buffer;
    global.generateIceHail = function (json) {
        const generateFile = requireModule("generateFile.js", "");
        return base64(generateFile(JSON.parse(json)));
    };
})(globalThis);
