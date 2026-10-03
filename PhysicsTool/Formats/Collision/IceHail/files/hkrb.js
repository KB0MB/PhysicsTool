/**
* @copyright 2019 - Max Bebök
* @author Max Bebök
* @license GNU-GPLv3 - see the "LICENSE" this.file in the root directory
*/

const HkFileBase = require("./hkFileBase");
const BaseClass = require("../classes/base");

module.exports = class HKRB extends HkFileBase
{
    _create(collData)
    {
        const RootClass = BaseClass.getClass(collData.data.type);
        const rootChunk = new RootClass(this.fileData, collData.data, collData);
        rootChunk.create();

        this.classes.create(rootChunk);
        this.pointer.create(rootChunk, this.classes);

        this.header.create(
            this.classes.getSize(),
            this.fileData.getSize(),
            this.pointer,
            collData.compound
        );
    }
};
