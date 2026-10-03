/**
* @copyright 2019 - Max Bebök
* @author Max Bebök
* @license GNU-GPLv3 - see the "LICENSE" this.file in the root directory
*/

module.exports = class Flags
{
    static getCollisionFlag(flags)
    {
        flags = flags || {};
        if(flags.climbable)
        {
            return 0x10000002;
        }

        return 0x10008002;
    }
};