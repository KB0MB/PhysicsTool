/**
* @copyright 2019 - Max Bebök
* @author Max Bebök
* @license GNU-GPLv3 - see the "LICENSE" this.file in the root directory
*/

const BaseClass = require("./base");
const BinaryFile = require("../binaryFile");
const {getCollisionFlag} = require("../flags");
const {writeVector4, readVector4} = require("../hkrbHelper");

/**
 * Capsule Shape
 * A capsule defined by 2 vertices
 * @extends {BaseClass}
 */
module.exports = class hkpCapsuleShape extends BaseClass
{
    _create()
    {
        this.file.skip(8);
        this.file.write('u32', 0x00040000);
        this.file.write('u32', getCollisionFlag(this.collData.flags));

        writeVector4(this.file, [this.collData.radius || 1.1, 0.0, 0.0, 0.0]);
        writeVector4(this.file, this.collData.vertices[0]);
        writeVector4(this.file, this.collData.vertices[1]);
    }

    read()
    {
        this.file.skip(16); // @TODO read flags
        const radius = this.file.read("float32");
        this.file.alignTo(16);

        return {
            radius,
            vertices: [
                readVector4(this.file),
                readVector4(this.file),
            ]
        };
    }
};