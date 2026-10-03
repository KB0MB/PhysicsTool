/**
* @copyright 2019 - Max Bebök
* @author Max Bebök
* @license GNU-GPLv3 - see the "LICENSE" this.file in the root directory
*/

const BaseClass = require("./base");
const {getCollisionFlag} = require("../flags");
const {writeVector4, readVector4} = require("../hkrbHelper");
const Quaternion = require("../math/quaternion");

const DEFAULT_RADIUS = 0.05;

/**
 * Transform Shape
 * A modifier to translate, scale and rotate a shape
 * @extends {BaseClass}
 */
module.exports = class hkpConvexTransformShape extends BaseClass
{
    /**
     * returns the offset for the linked-pointer list, depending of the child
     * @returns {number} pointer
     */
    getLinkPointer()
    {
        return this.offset + 0x18;
    }

    _create()
    {
        let {flags, radius} = this.collData;

        if(!radius) {
            const childData = this.getChildData();
            if(childData && childData.radius) {
                radius = childData.radius;
            }
        }

        this.file.skip(8);
        this.file.write('u32', 0x00040000);
        this.file.write('u32', getCollisionFlag(flags));

        const scale = this.collData.scale || [1.0, 1.0, 1.0];
        scale[3] = 1.0;

        writeVector4(this.file, [radius || 0.05, 0.0, 0.0, 0.0]);
        writeVector4(this.file, this.collData.translate || [0.0, 0.0, 0.0]);

        const rotate = this.collData.rotate || [0.0, 0.0, 0.0];
        if(rotate.length == 3) {
            writeVector4(this.file, Quaternion.fromEuler(...(rotate)));
        } else {
            writeVector4(this.file, rotate);
        }

        writeVector4(this.file, scale);
    }

    read()
    {
        this.file.skip(16); // @TODO read flags
        const data = {
            radius: this.file.read("float32")
        };

        this.file.skip(3 * 4);
        data.translate = readVector4(this.file);
        data.rotate = readVector4(this.file);
        data.scale = readVector4(this.file);

        if(data.radius.toFixed(6) == DEFAULT_RADIUS) {
            delete data.radius;
        }

        return data;
    }
};