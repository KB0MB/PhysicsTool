/**
* @copyright 2019 - Max Bebök
* @author Max Bebök
* @license GNU-GPLv3 - see the "LICENSE" this.file in the root directory
*/

const BaseClass = require("./base");
const {getCollisionFlag} = require("../flags");
const {writeVector4, readVector4} = require("../hkrbHelper");

const DEFAULT_RADIUS = 0.05;

/**
 * Box Shape
 * A simple box defined by a vector for its half-size
 * @extends {BaseClass}
 */
module.exports = class hkpBoxShape extends BaseClass
{
    _create()
    {
        this.file.skip(8);
        this.file.write('u32', 0x00040000);
        this.file.write('u32', getCollisionFlag(this.collData.flags));

        writeVector4(this.file, [this.collData.radius || DEFAULT_RADIUS, 0.0, 0.0, 0.0]);
        writeVector4(this.file, this.collData.sizeHalf);
    }


    read()
    {
        this.file.skip(16); // @TODO read flags
        const data = {
            radius: this.file.read("float32")
        };

        this.file.skip(3 * 4);
        data.sizeHalf = readVector4(this.file);

        if(data.radius.toFixed(6) == DEFAULT_RADIUS) {
            delete data.radius;
        }

        return data;
    }
};
