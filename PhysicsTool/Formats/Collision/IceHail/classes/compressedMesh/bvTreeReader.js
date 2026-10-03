/**
* @copyright 2019 - Max Bebök
* @author Max Bebök
* @license GNU-GPLv3 - see the "LICENSE" this.file in the root directory
*/

const {getSubDomain} = require('../bvTree/reader');

function readBvTree(file, count, domain)
{
    const treeArray = new Array(count).fill()
        .map(() => ({
            data: file.readArray('u8', 3),
            index: file.read('u8')
        }));

    return _bvTreeFromArray(treeArray, domain);
}

/**
 * Thanks to katalash for the tree logic
 * https://github.com/katalash/dstools/blob/master/SoulsFormats/Formats/HKX/Collision.cs
 */
function _bvTreeFromArray(treeArray, domain, index = 0)
{
    if(treeArray.length == 0) {
        throw "Compressed Mesh contains an empty BvTree";
    }

    if(index >= treeArray.length) {
        throw `Compressed Mesh BvTree, invalid index found '${index}'`;
    }

    const entry = treeArray[index];

    const node = {
        domain: getSubDomain(entry.data, domain),
    };

    if(entry.index & 0x01) {
        node.childA = _bvTreeFromArray(treeArray, node.domain, index + 1);
        node.childB = _bvTreeFromArray(treeArray, node.domain, index + (entry.index & 0xFE));
    } else {
        node.index = entry.index / 2;
    }

    return node;
}

module.exports = {readBvTree, _bvTreeFromArray};