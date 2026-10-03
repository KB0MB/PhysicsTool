/**
* @copyright 2019 - Max Bebök
* @author Max Bebök
* @license GNU-GPLv3 - see the "LICENSE" this.file in the root directory
*/

module.exports = class DataPointer
{
    // @TODO tests
    constructor(file, parent = undefined)
    {
        this.file = file;
        this.pos = this.file.pos();

        this.parent = parent;
        this.children = [];
    }

    add()
    {
        const ptr = new DataPointer(this.file, this);
        this.children.push(ptr);
        return ptr;
    }

    toArray()
    {
        const childData = this.children.flatMap(ptr => ptr.toArray());
        return this.parent ? [this.pos, ...childData] : childData;
    }

    toString(tab = "")
    {
        return [
            `${tab}└─${this.parent ? "" : "(root)"}${this.pos.toString(16)}`,
            ...this.children.map(ptr => ptr.toString(tab + "  "))
        ].join("\n");
    }
};