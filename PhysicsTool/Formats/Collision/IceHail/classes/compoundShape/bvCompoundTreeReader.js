/**
* @copyright 2019 - Max Bebök
* @author Max Bebök
* @license GNU-GPLv3 - see the "LICENSE" this.file in the root directory
*/

const {getSubDomain} = require('../bvTree/reader');

function readBvCompoundTree(file, count, domain)
{
    const treeArray = new Array(count).fill()
        .map(() => ({
            data: file.readArray('u8', 3),
            flags: file.read('u8'),
            index: file.read('u16'),
        }));

    return _bvCompoundTreeFromArray(treeArray, domain);
}

function _bvCompoundTreeFromArray(treeArray, domain, index = 0)
{
    if(treeArray.length == 0) {
        throw "Compressed Mesh contains an empty BvCompoundTree";
    }

    if(index >= treeArray.length) {
        throw `Compressed Mesh BvCompoundTree, invalid index found '${index}'`;
    }

    const entry = treeArray[index];

    const node = {
        domain: getSubDomain(entry.data, domain),
    };

    if(entry.flags == 0x80) {
        node.childA = _bvCompoundTreeFromArray(treeArray, node.domain, index + 1);
        node.childB = _bvCompoundTreeFromArray(treeArray, node.domain, index + entry.index * 2);
    } else{
        node.index = entry.index;
    }

    return node;
}

module.exports = {readBvCompoundTree, _bvCompoundTreeFromArray};
