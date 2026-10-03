/**
* @copyright 2019 - Max Bebök
* @author Max Bebök
* @license GNU-GPLv3 - see the "LICENSE" this.file in the root directory
*/

const BinaryFile = require("../binaryFile");
const DataPointer = require("../dataPointer");
const {writeCounter} = require('../hkrbHelper');

/**
 * Base class definition, used by all classes
 */
module.exports = class BaseClass
{
    /**
     * @param {BinaryFile} file
     * @param {Object} collData collision from json
     */
    constructor(file, collData = {}, globalCollData = undefined)
    {
        this.file = file;
        this.collData = collData;
        this.globalCollData = globalCollData || collData;

        this.offset = 0;

        this.dataPointer = new DataPointer(this.file);
        this.pointer = []; // pointer used in the first footer section
        this.children = []; // children chunks

        if (this.init) {
            this.init();
        }
    }

    constructNew(chunkClass, collData)
    {
        return new chunkClass(this.file, collData, this.globalCollData);
    }

    /**
     * adds a pointer to the pointer array, used in the footer section
     * @param {number} offset addition offset to add, default = 0
     */
    _addPointer(offset = 0)
    {
        this.pointer.push(this.file.pos() + offset);
    }

    /**
     * sets a pointer value
     * @param {number} index
     * @param {number} offset addition offset to add, default = 0
     */
    _setPointer(index, offset = 0)
    {
        this.pointer[index] = (this.file.pos() + offset);
    }

    /**
     * @param {BaseClass} child
     * @returns {BaseClass} added child
     */
    addChild(child)
    {
        this.children.push(child);
        return child;
    }

    /**
     * returns collision data from a child element,
     * @param {number} index
     * @returns {Object|undefined} data, undefined if no child was found
     */
    getChildData(index = 0)
    {
        if(this.collData.children && this.collData.children[index]) {
            return this.collData.children[index];
        }
    }

    /**
     * returns the offset for the linked-pointer list, depending of the child
     * @param {number} childNum
     * @returns {number} pointer
     */
    getLinkPointer(childNum)
    {
        return this.offset;
    }

    /**
     * writes the data to the file, this also writes all children
     */
    create()
    {
        this.offset = this.file.pos();

        this._create();

        this.pointer.push(...this.dataPointer.toArray());

        if(this.collData.children)
        {
            this.children = this.collData.children
                .map(coll => {
                    const CollClass = BaseClass.getClass(coll.type);
                    const child = new CollClass(this.file, coll, this.globalCollData);
                    child.create();
                    return child;
                });
        }
    }

    writeCounterPointer(count, ptr)
    {
        let ptrChild = undefined;
        if(count > 0) {
            ptrChild = (ptr || this.dataPointer).add();
        }
        writeCounter(this.file, count);
        return ptrChild;
    }

    static getClass(type)
    {
        if (!(/^[a-zA-Z0-9]+$/).test(type)) {
            throw `Error: invalid type name '${type}'`;
        }
        try{
            return require(`./${type}.js`);
        } catch(e) {
            console.error(`Error: Class '${type}' not supported and is ignored!`);
            console.error(e);
            throw `Unknown class '${type}'`;
        }
    }

    /**
     * actual implementation of create
     */
    _create()
    {
        throw "Class has no create function";
    }

    read()
    {
        return {};
    }
};