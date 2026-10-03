/**
* @copyright 2019 - Max Bebök
* @author Max Bebök
* @license GNU-GPLv3 - see the "LICENSE" this.file in the root directory
*/

const BaseClass = require("./base");
const BinaryFile = require("../binaryFile");
const {writeCounter, readCounter} = require("../hkrbHelper");

/**
 * Physics System
 * @extends {BaseClass}
 */
module.exports = class hkpPhysicsSystem extends BaseClass
{
    init()
    {
        this.defaultName = "Default Physics System";
    }

    /**
     * returns the offset for the linked-pointer list, depending of the child
     * @returns {number} pointer
     */
    getLinkPointer()
    {
        return this.offset + 0x50;
    }

    _create()
    {
        let compoundChildren = 1;
        if(this.globalCollData.compound) {
            const childCount = this.collData.children ? this.collData.children.length : 0;
            compoundChildren = Math.min(childCount, 1);
        }

        this.file.skip(4 * 2);

        if(compoundChildren > 0) {
            this._addPointer();
        }

        writeCounter(this.file, compoundChildren);
        writeCounter(this.file, [0, 0, 0]);
        this.file.alignTo(16);

        this.file.write("u8", 1); // unknown
        this.file.alignTo(16);

        if(compoundChildren > 0) {
            this._addPointer();
        }

        this._addPointer(-0x18);

        //for(let i=0; i<compoundChildren; ++i) {
        if(compoundChildren > 0) {
            this.file.skip(16);
        }

        this._addPointer();
        this.file.writeString(this.collData.name || this.defaultName);
        this.file.alignTo(16);
    }

    read(chunk)
    {
        this.file.skip(8);
        const countEntries = readCounter(this.file);
        readCounter(this.file);
        readCounter(this.file);
        readCounter(this.file);
        const offsetName = chunk.dataLinks.get(this.file.pos());

        this.file.pos(offsetName);
        return {
            name: this.file.readString()
        }
    }
};