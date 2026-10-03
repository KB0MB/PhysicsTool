/**
* @copyright 2019 - Max Bebök
* @author Max Bebök
* @license GNU-GPLv3 - see the "LICENSE" this.file in the root directory
*/

const BaseSection = require("./base");
const BinaryFile = require("../binaryFile");

module.exports = class Header extends BaseSection
{
    constructor()
    {
        super();
        this.sizeHeader = 0;
        this.file = new BinaryFile().setEndian("big");

        this.magicBuffer = Buffer.from([0x57, 0xE0, 0xE0, 0x57, 0x10, 0xC0, 0xC0, 0x10]);
    }

    create(sizeClasses, sizeData, pointerSection, extraData)
    {
        this.sizeHeader = 0x100 + (extraData ? 16 : 0);

        this._writeHeader(extraData);

        const offsetEOF = sizeData + pointerSection.getSize();

        this._writePointerSection("__classnames__", [
            this.sizeHeader, ...new Array(6).fill(sizeClasses)
        ]);

        this._writePointerSection("__types__", [
            this.sizeHeader + sizeClasses, ...new Array(6).fill(0)
        ]);

        this._writePointerSection("__data__", [
            this.sizeHeader + sizeClasses,
            sizeData,
            sizeData + pointerSection.offsetLinked,
            sizeData + pointerSection.offsetClassMapping,
            offsetEOF, offsetEOF, offsetEOF
        ]);

    }

    _writeHeader(extraData = false)
    {
        this.file.writeBuffer(this.magicBuffer);
        this.file.write('u32', 0); // unknown
        this.file.write('u32', 0x0B); // version?
        this.file.writeBuffer([4, 0, 0, 1]); // unknown
        this.file.write('u32', 3); // havok sections
        this.file.write('u32', 2); // unknown

        this.file.skip(11);

        this.file.writeString("Khk_2014.2.0-r1"); // engine version
        this.file.write('u8', 0xFF);

        this.file.write('u32', 0); // unknown

        if(extraData) {
            this.file.write('u32', 0x00150010); // extra data marker
            this.file.write('u32', 0x00140000);
            this.file.alignTo(16);
        }else{
            this.file.write('u32', 0x00150000); // extra data marker
        }
    }

    _writePointerSection(name, pointer)
    {
        this.file.writeString(name);
        this.file.alignTo(16);

        this.file.write('u32', 0xFF); // first pointer is always 0x000000FF

        for(let i=0; i<7; ++i) // 7 more pointers relative to the beginning of the data section
            this.file.write('u32', pointer[i]);

        for(let i=0; i<16; ++i) // padding for no reason
            this.file.write('u8', 0xFF);
    }

    read(hkFile)
    {
        const magic = hkFile.readBuffer(this.magicBuffer.length);
        if(Buffer.compare(magic, this.magicBuffer) != 0) {
            throw "Invalid file-Magic / Not a valid HK file";
        }

        hkFile.alignTo(16);
        hkFile.skip(47);
        const extraDataMarket = hkFile.read("u8");
        if(extraDataMarket) {
            hkFile.skip(16);
        }

        return {
            classes: this._readPointerSection(hkFile),
            types  : this._readPointerSection(hkFile),
            data   : this._readPointerSection(hkFile),
        };
    }

    _readPointerSection(hkFile)
    {
        const section = {
            name: hkFile.readString(),
            ptr: []
        };
        hkFile.alignTo(16);
        for (let i=0; i<8; ++i) {
            section.ptr[i] = hkFile.read("u32");
        }
        hkFile.skip(16);
        return section;
    }
};