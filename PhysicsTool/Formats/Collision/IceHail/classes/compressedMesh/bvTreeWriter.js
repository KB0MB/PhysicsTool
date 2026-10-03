/**
* @copyright 2019 - Max Bebök
* @author Max Bebök
* @license GNU-GPLv3 - see the "LICENSE" this.file in the root directory
*/

const {getSubDomain} = require('../bvTree/reader');
const {createSubDomain} = require('../bvTree/creator');

function writeBvTree(file, tree, parentDomain = undefined, index = 0)
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
        const posOffset = file.pos();
        file.write('u8', 0xFF);

        nodesWritten += writeBvTree(file, tree.childA, normDomain, index + nodesWritten);

        file.write('u8', nodesWritten | 1, posOffset);

        nodesWritten += writeBvTree(file, tree.childB, normDomain, index + nodesWritten);
    }else{
        file.write('u8', tree.index * 2);
    }

    return nodesWritten;
}

module.exports = {writeBvTree};