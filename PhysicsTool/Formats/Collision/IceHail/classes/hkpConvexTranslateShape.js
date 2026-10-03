/**
* @copyright 2019 - Max Bebök
* @author Max Bebök
* @license GNU-GPLv3 - see the "LICENSE" this.file in the root directory
*/

const BaseClass = require("./base");
const {getCollisionFlag} = require("../flags");
const {writeVector4, readVector4} = require("../hkrbHelper");

/**
 * Translate Shape
 * A modifier to translate a shape
 * @extends {BaseClass}
 */
module.exports = class hkpConvexTranslateShape extends BaseClass
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
        let {translate, flags, radius} = this.collData;

        if(!radius) {
            const childData = this.getChildData();
            if(childData && childData.radius) {
                radius = childData.radius;
            }
        }

        this.file.skip(8);
        this.file.write('u32', 0x00040000);
        this.file.write('u32', getCollisionFlag(flags));

        writeVector4(this.file, [radius || 0.05, 0.0, 0.0, 0.0]);
        writeVector4(this.file, translate || [0.0, 0.0, 0.0]);
    }

    read()
    {
        this.file.skip(16); // @TODO read flags
        const data = {
            radius: this.file.read("float32")
        };

        this.file.skip(3 * 4);
        data.translate = readVector4(this.file);

        return data;
    }
};