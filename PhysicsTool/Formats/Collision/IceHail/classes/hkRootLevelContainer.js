/**
* @copyright 2019 - Max Bebök
* @author Max Bebök
* @license GNU-GPLv3 - see the "LICENSE" this.file in the root directory
*/

const BaseClass = require("./base");
const BinaryFile = require("../binaryFile");
const {writeCounter, readCounter} = require("../hkrbHelper");

const PhysicsData = null;

/**
 * Base class definition, used by all classes
 * @extends {BaseClass}
 */
module.exports = class hkRootLevelContainer extends BaseClass
{
    /**
     * returns the offset for the linked-pointer list, depending of the child
     * @returns {number} pointer
     */
    getLinkPointer()
    {
        return this.offset + 0x18;
    }

    _createChildren()
    {
        return [this.constructNew(PhysicsData, this.collData)];
    }

    _create()
    {
        this._addPointer();

        writeCounter(this.file, 1); // physics system counter, always 1
        this.file.alignTo(16);
        this._addPointer();
        this._addPointer();

        this.file.skip(4 * 3);

        this._addPointer();
        this._addPointer(-8);

        this.file.writeString(this._getPhysicsDataName());
        this.file.alignTo(16);

        this._addPointer();
        this.file.writeString("hkpPhysicsData");
        this.file.alignTo(16);
    }

    read(chunk)
    {
        const offsetNameEntry = chunk.dataLinks.get(this.file.pos());
        const offsetName = chunk.dataLinks.get(offsetNameEntry);

        this.file.pos(offsetName);
        const name = this.file.readString();

        this.file.alignTo(16);

        return {
            name,
            physicsClass: this.file.readString()
        };
    }

    _getPhysicsDataName()
    {
        return this.globalCollData.compound
            ? "hkpPhysicsData"
            : "Physics Data";
    }
};