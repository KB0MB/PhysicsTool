/**
* @copyright 2018 - Max Bebök
* @author Max Bebök
* @license GNU-GPLv3 - see the "LICENSE" file in the root directory
*/

const fs = require('fs');

const DEFAULT_BUFFER_SIZE = (1024 * 1024) * 10; // 10MB
const BUFFER_SIZE_INCREMENT = (1024 * 1024) * 1; // 1MB

module.exports = class BinaryFile
{
    constructor(filePathOrBuffer = null, defaultSize = DEFAULT_BUFFER_SIZE)
    {
        let isBuffer = filePathOrBuffer == null || (filePathOrBuffer instanceof Buffer) || (filePathOrBuffer instanceof Uint8Array);

        // constants
        this.ENDIAN_LITTLE = 0;
        this.ENDIAN_BIG    = 1;

        // file settings
        this.filePath = isBuffer ? null : filePathOrBuffer;

        if(filePathOrBuffer == null)
        {
            this.buffer = Buffer.alloc(defaultSize);
            this.maxOffset = 0;
        }else{
            this.buffer = isBuffer ? filePathOrBuffer : fs.readFileSync(filePathOrBuffer);
            this.maxOffset = this.buffer.length;
        }

        this._readOnly = false;
        this.offset = 0;
        this.endian = this.ENDIAN_BIG;

        this.offsetStack = [];

        // data types
        this.types = {
            u8  : {
                read : [this.buffer.readUInt8, this.buffer.readUInt8],
                write: [this.buffer.writeUInt8, this.buffer.writeUInt8],
                size : 1
            },
            s8  : {
                read : [this.buffer.readInt8, this.buffer.readInt8],
                write: [this.buffer.writeInt8, this.buffer.writeInt8],
                size : 1
            },
            u16  : {
                read : [this.buffer.readUInt16LE, this.buffer.readUInt16BE],
                write: [this.buffer.writeUInt16LE, this.buffer.writeUInt16BE],
                size : 2
            },
            s16  : {
                read : [this.buffer.readInt16LE, this.buffer.readInt16BE],
                write: [this.buffer.writeInt16LE, this.buffer.writeInt16BE],
                size : 2
            },
            u32  : {
                read : [this.buffer.readUInt32LE, this.buffer.readUInt32BE],
                write: [
                    (value, offset) => this.buffer.writeUInt32LE(value >>> 0, offset),
                    (value, offset) => this.buffer.writeUInt32BE(value >>> 0, offset)
                ],
                size : 4
            },
            s32  : {
                read : [this.buffer.readInt32LE, this.buffer.readInt32BE],
                write: [this.buffer.writeInt32LE, this.buffer.writeInt32BE],
                size : 4
            },

            float16  : {
                read : [
                    offset => this.decodeFloat16(this.buffer.readUInt16LE(offset)),
                    offset => this.decodeFloat16(this.buffer.readUInt16BE(offset))
                ],
                write: [],
                size : 2
            },
            float32  : {
                read : [this.buffer.readFloatLE, this.buffer.readFloatBE],
                write: [this.buffer.writeFloatLE, this.buffer.writeFloatBE],
                size : 4
            },
            float64  : {
                read : [this.buffer.readDoubleLE, this.buffer.readDoubleBE],
                write: [this.buffer.writeDoubleLE, this.buffer.writeDoubleBE],
                size : 8
            },

            bool32 : {
                read : [
                    offset => this.buffer.readUInt32LE(offset) != 0,
                    offset => this.buffer.readUInt32BE(offset) != 0
                ],
                write: [
                    function (value, offset) { return this.writeUInt32LE(value ? 1 : 0, offset); },
                    function (value, offset) { return this.writeUInt32BE(value ? 1 : 0, offset); }
                ],
                size : 4
            },
            bool16 : {
                read : [
                    offset => this.buffer.readUInt16LE(offset) != 0,
                    offset => this.buffer.readUInt16BE(offset) != 0
                ],
                write: [
                    function (value, offset) { return this.writeUInt16LE(value ? 1 : 0, offset); },
                    function (value, offset) { return this.writeUInt16BE(value ? 1 : 0, offset); },
                ],
                size : 2
            },
            bool8 : {
                read : [
                    offset => this.buffer.readUInt8(offset) != 0,
                    offset => this.buffer.readUInt8(offset) != 0
                ],
                write: [
                    function (value, offset) { return this.writeUInt8(value ? 1 : 0, offset); },
                    function (value, offset) { return this.writeUInt8(value ? 1 : 0, offset); }
                ],
                size : 1
            },
        };

        this.types["char"]   = this.types.u8;
        this.types["float"]  = this.types.float32;
        this.types["double"] = this.types.float64;
        this.types["bool"]   = this.types.bool32;
    }

    readOnly(isReadOnly)
    {
        this._readOnly = !!isReadOnly;
        return this;
    }

    /**
     * Taken from on:
     * @author https://stackoverflow.com/a/5684578
     */
    decodeFloat16(binary)
    {
        let exponent = (binary & 0x7C00) >> 10;
        let fraction = binary & 0x03FF;

        return (binary >> 15 ? -1 : 1) * (
            exponent ?
            (
                exponent === 0x1F ?
                fraction ? NaN : Infinity :
                Math.pow(2, exponent - 15) * (1 + fraction / 0x400)
            ) :
            6.103515625e-5 * (fraction / 0x400)
        );
    }

    setEndian(endian)
    {
        if(endian == "big" || endian == this.ENDIAN_BIG)
            this.endian = this.ENDIAN_BIG;
        else if(endian == "little" || endian == this.ENDIAN_LITTLE)
            this.endian = this.ENDIAN_LITTLE;

        return this;
    }

    getBuffer(start = 0)
    {
        return this.buffer.slice(start, this.maxOffset);
    }

    getBufferSlice(size, pos = this.pos)
    {
        return this.buffer.slice(pos, pos + size);
    }

    getSize()
    {
        return this.maxOffset;
    }

    posPush()
    {
        this.offsetStack.push(this.pos());
    }

    posPop()
    {
        let offset = this.offsetStack.pop();
        if(offset != null)
            this.pos(offset);
    }

    alignTo(bytes, fillByte = 0)
    {
        const targetPos = Math.ceil(this.pos() / bytes) * bytes;
        for(let i=this.pos(); i<targetPos; ++i) {
            this.write('u8', fillByte);
        }

        if(this.offset >= this.maxOffset)
            this.maxOffset = Math.max(0, this.offset);
    }

    skip(bytes)
    {
        this.pos(this.offset + bytes);
    }

    fill(byte, length)
    {
        for(let i=0; i<length; ++i) {
            this.write('u8', byte);
        }
    }

    pos(newPos = -1)
    {
        if(newPos >= 0)
            this.offset = newPos;

        if(this.offset >= this.maxOffset)
            this.maxOffset = Math.max(0, this.offset);

        return this.offset;
    }

    read(type, offset = -1)
    {
        let pos = offset < 0 ? this.offset : offset;

        if(pos >= this.buffer.length) {
            console.log("PEOF: " + pos);
            throw "BinaryFile: EOF reached!";
        }

        let typeObj = this.types[type];
        if(typeObj == null)
            return null;

        let val = typeObj.read[this.endian].call(this.buffer, pos);

        if(offset < 0)
            this.offset += typeObj.size;

        return val;
    }

    readArray(type, length)
    {
        const data = new Array(length);
        for(let i=0; i<length; ++i) {
            data[i] = this.read(type);
        }
        return data;
    }

    // @TODO might not work as intended on little-endian!
    readBitString(bytes) {
        let res = "";
        for(let i=0; i<bytes; ++i) {
            res += this.read("u8").toString(2).padStart(8, "0");
        }
        return res;
    }

    readBuffer(length)
    {
        if(this.offset >= this.buffer.length) {
            throw "BinaryFile: EOF";
        }

        const res = this.buffer.slice(this.offset, length);
        this.offset += length;

        return res;
    }

    readString(size = -1, offset = -1)
    {
        let pos = offset < 0 ? this.offset : offset;
        let zeroEnding = false;

        if(size < 0)
        {
            let strBuff = this.buffer.slice(pos);
            size = strBuff.findIndex(val => val==0);
            if(size < 0) {
                size = this.buffer.length - pos;
            }
            zeroEnding = true;
        }

        let res = this.buffer.toString("utf8", pos, pos + size);

        if(offset < 0)
            this.offset += size + (zeroEnding ? 1 : 0);

        return res;
    }

    convert(type, value)
    {
        let typeObj = this.types[type];
        if(typeObj == null)
            return null;

        let resBuffer = Buffer.alloc(typeObj.size);
        typeObj.write[this.endian].call(resBuffer, value);

        return resBuffer;
    }

    write(type, value, offset = -1)
    {
        if(value instanceof Array) {
            return value.forEach(val => this.write(type, val, offset));
        }

        let pos = offset < 0 ? this.offset : offset;

        let typeObj = this.types[type];
        if(typeObj == null)
            return null;

        if((pos + typeObj.size) >= this.buffer.length) {
            this.resizeBuffer(this.buffer.length + BUFFER_SIZE_INCREMENT);
        }

        let bytesWritten = typeObj.size;
        if(!this._readOnly) {
            bytesWritten = typeObj.write[this.endian].call(this.buffer, value, pos);
        }

        if(offset < 0)
            this.offset += typeObj.size;

        if(pos > this.maxOffset)this.maxOffset = pos;
        if(this.offset > this.maxOffset)this.maxOffset = this.offset;

        return bytesWritten;
    }

    writeString(str, offset = -1)
    {
        let i=0;
        var strBuffer = Buffer.from(str, 'utf8');
        this.writeBuffer(strBuffer, offset);
        this.write("u8", 0, offset < 0 ? -1 : (offset + strBuffer.length));
    }

    writeBuffer(buff, offset = -1)
    {
        let i=0;
        for(let val of buff)
        {
            this.write("u8", val, offset < 0 ? -1 : (offset+i));
            ++i;
        }
    }

    resizeBuffer(newSize)
    {
        const newBuffer = Buffer.alloc(newSize);
        this.buffer.copy(newBuffer);
        this.buffer = newBuffer;
    }

    clear(fillOldWithZero = false)
    {
        if(fillOldWithZero && this.buffer)
        {
            this.buffer.fill(0);
        }

        this.buffer = Buffer.alloc(0);
    }

    createSubFile(start = 0, end = -1)
    {
        const subBuffer = this.buffer.slice(start, end >= 0 ? end : this.maxOffset);
        const file = new BinaryFile(subBuffer);
        file.setEndian(this.endian);
        file.readOnly(this._readOnly);
        return file;
    }
};
