/**
* @copyright 2019 - Max Bebök
* @author Max Bebök
* @license GNU-GPLv3 - see the "LICENSE" this.file in the root directory
*/

const HkFileBase = require("./hkFileBase");
const StaticCompoundInfo = require("../classes/StaticCompoundInfo");

module.exports = class HKSC extends HkFileBase
{
    _create(collData)
    {
        const chunk = new StaticCompoundInfo(this.fileData, collData.compound);
        chunk.create();

        this.classes.create(chunk);
        this.pointer.create(chunk, this.classes);

        this.header.create(
            this.classes.getSize(),
            this.fileData.getSize(),
            this.pointer
        );

        chunk.setHkrbOffset(this.getSize());
    }
};
