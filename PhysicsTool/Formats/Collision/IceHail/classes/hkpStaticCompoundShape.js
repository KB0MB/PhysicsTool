/**
* @copyright 2019 - Max Bebök
* @author Max Bebök
* @license GNU-GPLv3 - see the "LICENSE" this.file in the root directory
*/

const BaseClass = require("./base");
const {writeVector4, writeCounter, readCounter, readVector4} = require("../hkrbHelper");
const Quaternion = require("../math/quaternion");

const {createBvTreeFromEntries} = require('./bvTree/creator');
const {readBvCompoundTree} = require('./compoundShape/bvCompoundTreeReader');
const {writeBvCompoundTree} = require('./compoundShape/bvCompoundTreeWriter');

/**
 * Static Compound Shape
 * Container for multiple chunks that are linked to actors
 * @extends {BaseClass}
 */
module.exports = class hkpStaticCompoundShape extends BaseClass
{
    /**
     * returns the offset for the linked-pointer list, depending of the child
     * @returns {number} pointer
     */
    getLinkPointer(num)
    {
        return this.offsetActors + (num * 0x40) + 0x30;
    }

    _create()
    {
        this.file.skip(8);
        this.file.write('u32', 0x00040000);
        this.file.write('u32', 0);

        this.file.write('u32', 0x02000000);
        this.file.write('u32', 0);
        this.file.write('u32', 0x08000000); // 0D, 0C, 0B
        this.file.write('u32', 0);

        const tree = createBvTreeFromEntries(this.collData.children);

        const entryCount = this.collData.children.length;
        const treeCount = entryCount * 2 - 1;
        //this.dataCount = entryCount;

        this._setPointer(0);
        writeCounter(this.file, this.collData.children.length);
        writeCounter(this.file, 0); // unknown counter
        this.file.alignTo(16);

        this._setPointer(2);
        writeCounter(this.file, treeCount);
        this.file.alignTo(16);

        writeVector4(this.file, tree.domain.min);
        writeVector4(this.file, tree.domain.max);

        this._setPointer(1);
        this.offsetActors = this.file.pos();
        this.collData.children.forEach(entry => this._writeActorEntry(entry))

        this._setPointer(3);
        writeBvCompoundTree(this.file, tree, tree.domain);
        this.file.alignTo(16);
    }

    _writeActorEntry(entry)
    {
        const pos = entry.translate || [0.0, 0.0, 0.0];
        pos[3] = 0.5000001; // just 0.5 makes it invisible

        const rot = entry.rotate ? Quaternion.fromEuler(...entry.rotate) : [0,0,0];
        rot[3] = 1.0;
        //const rot = entry.rotate || Quaternion.fromEuler(0, 0, 0);

        const scale = entry.scale || [1.0, 1.0, 1.0];
        scale[3] = 0.5;

        writeVector4(this.file, pos);
        writeVector4(this.file, rot);
        writeVector4(this.file, scale);

        this.file.write('u32', 0);

        switch(entry.type)
        {
            case 'hkpBoxShape':
                this.file.write('u32', 0x90018000);
                this.file.write('u32', 0);
            break;
            default:
                this.file.write('u32', 0);
                this.file.write('u32', 0xFFFFFFFF);
            break;
        }

        this.file.write('u32', entry.actorId);
    }

    read(chunk)
    {
        this.file.skip(8); // padding
        this.file.skip(4 * 6); // some unknown ints

        const offsetEntries = chunk.dataLinks.get(this.file.pos());
        const countEntries = readCounter(this.file);
        const countUnknown0 = readCounter(this.file);

        this.file.alignTo(16);

        const offsetBvCompoundTree = chunk.dataLinks.get(this.file.pos());
        const countBvCompoundTree = readCounter(this.file);

        this.file.alignTo(16);

        const domain = {
            min: readVector4(this.file),
            max: readVector4(this.file),
        };
        const entries = new Array(countEntries).fill()
            .map(() => this._readEntry());

        //this.file.pos(offsetBvCompoundTree);
        //const tree = readBvCompoundTree(this.file, countBvCompoundTree, domain);

        return {domain, entries};
    }

    _readEntry(chunk)
    {
        return {
            translate: readVector4(this.file),
            rotate: readVector4(this.file),
            scale: readVector4(this.file),
            flags: this.file.readArray("u32", 3),
            actorId: this.file.read('u32')
        };
    }
};
