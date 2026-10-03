/**
* @copyright 2019 - Max Bebök
* @author Max Bebök
* @license GNU-GPLv3 - see the "LICENSE" this.file in the root directory
*/

const BaseClass = require("./base");
const BinaryFile = require("../binaryFile");
const {writeCounter} = require("../hkrbHelper");

/**
 * Base class definition, used by all classes
 * @extends {BaseClass}
 */
module.exports = class hkpPhysicsData extends BaseClass
{
    /**
     * returns the offset for the linked-pointer list, depending of the child
     * @param {number} childNum
     * @returns {number} pointer
     */
    getLinkPointer(childNum)
    {
        return this.offset + 0x20 + (childNum * 4);
    }

    _create()
    {
        this.file.skip(4 * 3);

        this._addPointer();
        writeCounter(this.file, this.collData.children.length); // chunk counter

        this.file.alignTo(16);
        this._addPointer();

        this.file.skip(0x10);
        if(this.globalCollData.compound) {
            this.file.skip(0x10 * 4);
        }
    }
};