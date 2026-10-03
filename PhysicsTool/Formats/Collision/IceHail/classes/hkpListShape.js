/**
* @copyright 2019 - Max Bebök
* @author Max Bebök
* @license GNU-GPLv3 - see the "LICENSE" this.file in the root directory
*/

const BaseClass = require("./base");
const BinaryFile = require("../binaryFile");
const BoundingBox = require("../math/bounding-box");
const {writeCounter, writeVector4} = require("../hkrbHelper");

/**
 * List Shape
 * Container to combine multiple shapes and types together
 * @extends {BaseClass}
 */
module.exports = class hkpListShape extends BaseClass
{
    init()
    {
        this.w0 = 1.0;
        this.w1 = 0.05;
    }

    /**
     * returns the offset for the linked-pointer list, depending of the child
     * @returns {number} pointer
     */
    getLinkPointer(num)
    {
        return this.offset + 0x70 + (num * 16);
    }

    _create()
    {
        this.file.skip(4 * 2);
        this.file.write('u32', 0x00040000);
        this.file.skip(4 * 3);

        this._addPointer();
        writeCounter(this.file, this.collData.children.length);
        this.file.alignTo(16);

        const globalBox = BoundingBox.fromTree(this.collData).scale(1.05).getAsObject();

        writeVector4(this.file, [...globalBox.sizeHalf, this.w0]);
        writeVector4(this.file, [...globalBox.center, this.w1]);

        this.file.fill(0xFF, 32);

        this._addPointer();
        this.collData.children.forEach(() => {
            writeVector4(this.file, [0.0, 0.007812507, 0.0]);
        });
    }
};