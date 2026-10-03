/**
* @copyright 2019 - Max Bebök
* @author Max Bebök
* @license GNU-GPLv3 - see the "LICENSE" this.file in the root directory
*/

const BaseClass = require("./base");
const {getCollisionFlag} = require("../flags");
const {writeVector4} = require("../hkrbHelper");

/**
 * Sphere Shape
 * A simple sphere defined by its radius
 * @extends {BaseClass}
 */
module.exports = class hkpSphereShape extends BaseClass
{
    _create()
    {
        this.file.skip(8);
        this.file.write('u32', 0x00040000);
        this.file.write('u32', getCollisionFlag(this.collData.flags));

        writeVector4(this.file, [this.collData.radius]);
    }

    read()
    {
        this.file.skip(16); // @TODO read flags
        return {
            radius: this.file.read("float32")
        };
    }
};