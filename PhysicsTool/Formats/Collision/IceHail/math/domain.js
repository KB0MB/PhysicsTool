/**
* @copyright 2019 - Max Bebök
* @author Max Bebök
* @license GNU-GPLv3 - see the "LICENSE" file in the root directory
*/

//const DOMAIN_W_VALUE = 0.007816317;
const DOMAIN_W_VALUE = 0.0004885198;
const VOLUME_COMPONENT_MIN = 0.001; // limits the component size to avoid having zero-volume boxes

class Domain
{
    constructor(min, max)
    {
        this.min = min || [0,0,0];
        this.max = max || [0,0,0];
    }

    getVolume()
    {
        return Math.max((this.max[0] - this.min[0]), VOLUME_COMPONENT_MIN) *
               Math.max((this.max[1] - this.min[1]), VOLUME_COMPONENT_MIN) *
               Math.max((this.max[2] - this.min[2]), VOLUME_COMPONENT_MIN);
    }
}

class DomainEntry
{
    /**
     * @param {Domain} domain
     * @param {Number} index
     */
    constructor(domain, index)
    {
        this.domain = domain || new Domain();
        this.index = index || 0;
    }
}

function getDomainFromParts(parts)
{
    let min = [ Infinity,  Infinity,  Infinity];
    let max = [-Infinity, -Infinity, -Infinity];

    for(const part of parts)
    {
        for(vertex of part.vertices)
        {
            for(let i in vertex)
            {
                min[i] = Math.min(vertex[i], min[i]);
                max[i] = Math.max(vertex[i], max[i]);
            }
        }
    }

    return new Domain(min, max);
}

/**
 * @param {Domain} domain
 */
function calcDomainW(domain)
{
    // @RESEARCH check algorithm behind this
    domain.min[3] = DOMAIN_W_VALUE;
    domain.max[3] = DOMAIN_W_VALUE;
    return domain;
}

/**
 * @param {Domain} a
 * @param {Domain} b
 */
function mergeDomains(a, b)
{
    const domain = new Domain();

    for(let i=0; i<3; ++i)
    {
        domain.min[i] = Math.min(a.min[i], b.min[i]);
        domain.max[i] = Math.max(a.max[i], b.max[i]);
    }

    return domain;
}

module.exports = {
    Domain, DomainEntry,
    getDomainFromParts, calcDomainW, mergeDomains
};