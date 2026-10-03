/**
* @copyright 2019 - Max Bebök
* @author Max Bebök
* @license GNU-GPLv3 - see the "LICENSE" this.file in the root directory
*/

const BvTree = require('./tree');
const {Domain, DomainEntry, mergeDomains} = require('../../math/domain');

/**
 * @param {DomainEntry[]} entries
 */
function buildTree(entries, parentTree = undefined)
{
    if(!entries) {
        return undefined;
    }
    if(entries.length <= 1) {
        return parentTree || entries[0];
    }

    const minEntries = _getMinDomainEntries(entries);
    const treeNode = new BvTree(minEntries[2], entries[minEntries[0]], entries[minEntries[1]]);

    if(minEntries[0] == minEntries[1]) {
        throw "Invalid entries in bvTree creation, A nd B are the same!";
    }

    const newEntries = entries.filter(
        (val, idx) => idx != minEntries[0] && idx != minEntries[1]
    );
    newEntries.push(treeNode);

    return buildTree(newEntries, treeNode);
}

/**
 * tries to combine every entry an returns the entry-index with the smallest domain
 * @param {[DomainEntry]} entries
 * @returns {[Number, Number, Domain]}
 */
function _getMinDomainEntries(entries)
{
    if(entries.length == 0) {
        return undefined;
    }

    let minEntries = [0, 0, entries[0].domain];
    let minDomainVolume = Infinity;

    for(let i=0; i<entries.length; ++i)
    {
        const domainA = entries[i].domain;
        for(let j=i+1; j<entries.length; ++j)
        {
            const combinedDomain = mergeDomains(domainA, entries[j].domain);
            const volume = combinedDomain.getVolume();
            if(volume < minDomainVolume) {
                minDomainVolume = volume;
                minEntries = [i, j, combinedDomain];
            }
        }
    }
    return minEntries;
}

module.exports = {buildTree, _getMinDomainEntries};