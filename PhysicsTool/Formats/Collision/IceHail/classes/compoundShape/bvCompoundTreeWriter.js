/**
* @copyright 2019 - Max Bebök
* @author Max Bebök
* @license GNU-GPLv3 - see the "LICENSE" this.file in the root directory
*/

const {getSubDomain} = require('../bvTree/reader');
const {createSubDomain} = require('../bvTree/creator');

function writeBvCompoundTree(file, tree, parentDomain = undefined, index = 0)
{
    let nodesWritten = 1;
    const domainBytes = createSubDomain(parentDomain, tree.domain);
    let normDomain = tree.domain;

    if(parentDomain) {
        normDomain = getSubDomain(domainBytes, parentDomain);
    }

    file.write('u8', domainBytes);

    if(tree.childA && tree.childB)
    {
        file.write('u8', 0x80);

        const posOffset = file.pos();
        file.write('u16', 0xFFFF);

        nodesWritten += writeBvCompoundTree(file, tree.childA, normDomain, index + nodesWritten);

        file.write('u16', nodesWritten / 2, posOffset);

        nodesWritten += writeBvCompoundTree(file, tree.childB, normDomain, index + nodesWritten);
    }else{
        file.write('u8', 0x00);
        file.write('u16', tree.index);
    }

    return nodesWritten;
}

module.exports = {writeBvCompoundTree};