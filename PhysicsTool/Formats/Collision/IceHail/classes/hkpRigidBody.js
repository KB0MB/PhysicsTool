/**
* @copyright 2019 - Max Bebök
* @author Max Bebök
* @license GNU-GPLv3 - see the "LICENSE" this.file in the root directory
*/

const BaseClass = require("./base");
const {writeCounter, writeVector4, writeMatrix4x4} = require("../hkrbHelper");

/**
 * Rigid Body
 * I have no idea what most of the data here means
 * @extends {BaseClass}
 */
module.exports = class hkpRigidBody extends BaseClass
{
    /**
     * returns the offset for the linked-pointer list, depending of the child
     * @returns {number} pointer
     */
    getLinkPointer()
    {
        return this.offset + 0x10;
    }

    _create()
    {
        this.file.skip(16);

        this.file.write('u32', 0x00000000);
        this.file.write('u32', 0xFFFFFFFF);

        this.file.write('u32', 0x00000000);

        this.file.write('u32', 0x00000000);
        this.file.write('u32', 0x00080000);

        this.file.write('u32', 0x00000000);
        this.file.write('u32', 0x01000000);
        this.file.write('float32', 0.007812507);

        this.file.skip(16 * 2);

        this.file.skip(4 * 3);
        this.file.write('u32', 0x7F7FFFEE);
        writeCounter(this.file, 0); // unknown counter

        this.file.skip(12);
        this._addPointer();
        this.file.skip(12);

        this.file.write('u32', 0x80000000);
        this.file.write('u32', 0x01000000);

        writeVector4(this.file, [0.5, 0.4, 0.0, 1.0]);

        this.file.skip(8);
        this.file.write('u32', 0xFFFFFFFF);
        this.file.alignTo(16);

        writeCounter(this.file, 0); // unknown counter
        writeCounter(this.file, 0); // unknown counter

        this.file.write('u32', 0x00000000);
        this.file.write('u32', 0x00020000);

        this.file.write('u32', 0xFFFFFFFF);
        this.file.write('u32', 0x00000000);

        this.file.write('u32', 0x301);
        this.file.write('u32', 0x00000000);

        writeVector4(this.file, [0.0, 0.0, 0.0, -2.0]);
        writeMatrix4x4(this.file, [
            [1.0, 0.0, 0.0, 0.0],
            [0.0, 1.0, 0.0, 0.0],
            [0.0, 0.0, 1.0, 0.0],
            [0.0, 0.0, 0.0, 1.0],
        ]);

        writeMatrix4x4(this.file, [
            [0.0, 0.0, 0.0, 0.0],
            [0.0, 0.0, 0.0, 0.0],
            [0.0, 0.0, 0.0, 0.999999],
            [0.0, 0.0, 0.0, 0.999999],
        ]);

        this.file.skip(32);

        this.file.write('float32', 8.0);
        this.file.write('u32', 0x00003D4D);
        this.file.write('float32', 1.0);
        this.file.write('u32', 0x01000000);

        this.file.skip(16 * 5);

        this.file.skip(14);
        this.file.write('float32', 1.0);
        this.file.alignTo(16);

        this.file.skip(8);
        this.file.write('u32', 0xFFFFFFFF);
        this.file.alignTo(16);

        this._addPointer();
        this.file.writeString(this.collData.name);
        this.file.alignTo(16);
    }

    read()
    {
        this.file.skip(0x220);
        return {
            name: this.file.readString()
        };
    }
};