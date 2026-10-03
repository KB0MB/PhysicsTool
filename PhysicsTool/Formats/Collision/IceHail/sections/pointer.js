/**
* @copyright 2019 - Max Bebök
* @author Max Bebök
* @license GNU-GPLv3 - see the "LICENSE" this.file in the root directory
*/

const BaseSection = require("./base");
const BinaryFile = require("../binaryFile");

module.exports = class Pointer extends BaseSection
{
    constructor()
    {
        super();
        this.file = new BinaryFile().setEndian("big");

        this.offsetLinked = 0;
        this.offsetClassMapping = 0;
    }

    create(chunk, classes)
    {
        this._createData(chunk);
        this.file.alignTo(16, 0xFF);

        this.offsetLinked = this.file.pos();
        this._createLinked(chunk);
        this.file.alignTo(16, 0xFF);

        this.offsetClassMapping = this.file.pos();
        this._createClassMapping(chunk, classes);
        this.file.alignTo(16, 0xFF);
    }

    _createData(chunk)
    {
        chunk.pointer.forEach(ptr => this.file.write('u32', ptr));
        chunk.children.forEach(child => this._createData(child));
    }

    _createLinked(chunk)
    {
        chunk.children.forEach(
            (child, i) => {
                this.file.write('u32', chunk.getLinkPointer(i));
                this.file.write('u32', 2);
                this.file.write('u32', child.offset);
            }
        );
        chunk.children.forEach(child => this._createLinked(child));
    }

    _createClassMapping(chunk, classes)
    {
        this.file.write('u32', chunk.offset);
        this.file.write('u32', 0);
        this.file.write('u32', classes.getOffset(chunk.constructor.name));

        chunk.children.forEach(child => this._createClassMapping(child, classes));
    }

    read(hkFile, dataPointer)
    {
        const offsetData  = dataPointer[1] + dataPointer[2];
        const offsetLink  = dataPointer[1] + dataPointer[3];
        const offsetClass = dataPointer[1] + dataPointer[4];
        const EOF = dataPointer[1] + dataPointer[5];

        const dataLinks = this._readData(hkFile, offsetData, offsetLink, dataPointer[1]);
        const chunkLinks = this._readLinked(hkFile, offsetLink, offsetClass,  dataPointer[1]);

        let classMapping = this._readLinked(hkFile, offsetClass, EOF)
            .map((entry, i, arr) => {
                const nextPos = (arr[i+1] ? arr[i+1][0] : dataPointer[2]);
                return {
                    start: dataPointer[1] + entry[0],
                    end: dataPointer[1] + nextPos - 1,
                    classNameOffset: entry[1],
                    className: "",
                    dataLinks,
                    children: []
                };
            });

        chunkLinks.forEach(link =>
        {
            classMapping.forEach(chunk =>
            {
                if(link[0] >= chunk.start && link[0] < chunk.end)
                {
                    classMapping.forEach(chunkLink => {
                        if(link[1] == chunkLink.start)
                        {
                            chunk.children.push(chunkLink);
                            return;
                        }
                    });
                }
            });
        });

        return classMapping[0];
    }

    _readData(hkFile, offsetStart, offsetEnd, globalOffset = 0)
    {
        const data = new Map();
        for(hkFile.pos(offsetStart); hkFile.pos() < offsetEnd;)
        {
            const dataCounter = hkFile.read("u32");
            if(dataCounter == 0xFFFFFFFF)break;
            data.set(dataCounter + globalOffset, hkFile.read("u32") + globalOffset);
        }
        return data;
    }

    _readLinked(hkFile, offsetStart, offsetEnd, globalOffset = 0)
    {
        const classMapping = [];
        for(hkFile.pos(offsetStart); hkFile.pos() < offsetEnd;)
        {
            const dataPtr = hkFile.read("u32");
            if(dataPtr == 0xFFFFFFFF) {
                break;
            }
            hkFile.skip(4);
            classMapping.push([
                dataPtr + globalOffset,
                hkFile.read("u32") + globalOffset
            ]);
        }
        return classMapping;
    }
};