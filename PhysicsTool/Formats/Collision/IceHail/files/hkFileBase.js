/**
* @copyright 2019 - Max Bebök
* @author Max Bebök
* @license GNU-GPLv3 - see the "LICENSE" this.file in the root directory
*/

const BinaryFile = require("../binaryFile");
const fileToJson = require("../fileToJson");
const BaseClass = require("../classes/base");

const Header = require("../sections/header");
const Classes = require("../sections/classes");
const Pointer = require("../sections/pointer");

module.exports = class HkFileBase
{
    constructor()
    {
        this.fileData = new BinaryFile().setEndian("big");
        this.header = new Header();
        this.classes = new Classes();
        this.pointer = new Pointer();

        this.child = null;
    }

    create(collData)
    {
        if(collData.version != 4) {
            throw "Incompatible file version, make sure the version is '4'";
        }

        this._create(collData);
    }

    read(hkFile)
    {
        const headerSection = this.header.read(hkFile);

        hkFile.pos(headerSection.classes.ptr[1]);
        this.classes.read(hkFile, headerSection.classes.ptr[2]);

        const classTree = this.pointer.read(hkFile, headerSection.data.ptr);
        this._readChunk(classTree, hkFile);

        return fileToJson(classTree);
    }

    _readChunk(chunk, hkFile)
    {
        chunk.className = this.classes.getName(chunk.classNameOffset);
        if(!chunk.className) {
            throw "Cannot read classname for chunk";
        }

        hkFile.pos(chunk.start);
        const chunkClass = BaseClass.getClass(chunk.className);
        if(!chunkClass) {
            console.error(`Unknown class '${chunk.className}'`);
        } else {
            const chunkReader = new chunkClass(hkFile);
            chunk.data = chunkReader.read(chunk);
        }

        chunk.children.map(child => this._readChunk(child, hkFile));
    }

    setChild(hkFile)
    {
        this.child = hkFile;
    }

    getBuffer()
    {
        return Buffer.concat([
            this.header.getBuffer(),
            this.classes.getBuffer(),
            this.fileData.getBuffer(),
            this.pointer.getBuffer(),
            this.child ? this.child.getBuffer() : Buffer.alloc(0)
        ]);
    }

    getSize()
    {
        return this.header.getBuffer().length +
            this.classes.getBuffer().length +
            this.fileData.getBuffer().length +
            this.pointer.getBuffer().length +
            (this.child ? this.child.getSize() : 0)
    }
};
