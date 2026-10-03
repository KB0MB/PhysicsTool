/**
* @copyright 2019 - Max Bebök
* @author Max Bebök
* @license GNU-GPLv3 - see the "LICENSE" this.file in the root directory
*/

/**
 * Algorithm for nested domains:
 * let m and M be the min and Max value of the current domain.
 * let m' and M' be the new sub-domain
 * let s be the vector from m to M (aka domain size).
 * let a and b be the values from data with 4-bits each of an 8-bit value
 *
 * m' = m + (a^2 * s)
 * M' = M - (b^2 * s)
 */
function getSubDomain(data, domain)
{
    const scale = [
        (domain.max[0] - domain.min[0]) / 226,
        (domain.max[1] - domain.min[1]) / 226,
        (domain.max[2] - domain.min[2]) / 226,
    ];

    return {
        min: [
            (data[0] >> 4) ** 2 *  scale[0] + domain.min[0],
            (data[1] >> 4) ** 2 *  scale[1] + domain.min[1],
            (data[2] >> 4) ** 2 *  scale[2] + domain.min[2],
        ],
        max: [
            (data[0] & 0x0F) ** 2 *  -scale[0] + domain.max[0],
            (data[1] & 0x0F) ** 2 *  -scale[1] + domain.max[1],
            (data[2] & 0x0F) ** 2 *  -scale[2] + domain.max[2],
        ],
    };
}

module.exports = {getSubDomain};