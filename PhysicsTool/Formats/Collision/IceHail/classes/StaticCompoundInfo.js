/**
* @copyright 2019 - Max Bebök
* @author Max Bebök
* @license GNU-GPLv3 - see the "LICENSE" this.file in the root directory
*/

const BaseClass = require("./base");
const {writeCounter} = require("../hkrbHelper");

/**
 * StaticCompoundInfo
 * A container with a list of actors (id, hash, index)
 * @extends {BaseClass}
 */
module.exports = class StaticCompoundInfo extends BaseClass
{
    /**
     * Sets the absolute offset where the HKRB file starts
     */
    setHkrbOffset(hkrbOffset)
    {
        this.file.write('u32', hkrbOffset, 0);
    }

    _create()
    {
        const {actors} = this.collData;
        this.file.write('u32', 0xFFFFFFFF); // HKRB-offset, is set later

        this._setPointer(0);
        writeCounter(this.file, actors.length);

        this._setPointer(2);
        writeCounter(this.file, actors.length);

        this.file.alignTo(16);

        this._setPointer(1);
        actors.forEach(actor => this._writeActor(actor));

        this._setPointer(3);
        actors.forEach(actor => this._writeActorLink(actor));

        this.file.alignTo(16);

        if(actors.length == 0) {
            this.pointer = [];
        }
    }

    _writeActor(actor)
    {
        this.file.write('u32', parseInt(actor.hashId, 16));
        this.file.write('u32', parseInt(actor.strHash, 16));
        this.file.write('u32', actor.id);
        this.file.write('u32', actor.id);
    }

    _writeActorLink(actor)
    {
        this.file.write('u32', actor.id);
        this.file.write('u32', actor.id);
        this.file.write('u32', 0);
    }

    read(chunk)
    {
        const eof = this.file.read('u32');

        // @TODO read data

        this.file.pos(eof);
    }
};