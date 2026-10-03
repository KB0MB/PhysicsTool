/**
* @copyright 2019 - Max Bebök
* @author Max Bebök
* @license GNU-GPLv3 - see the "LICENSE" file in the root directory
*/

function readIndices(file, count)
{
    const res = new Array(count);

    for(let i=0; i<count; ++i) {
        let val = file.read("u32");
        res[i] = [
            (val >> 24) & 0xFF,
            (val >> 16) & 0xFF,
            (val >>  8) & 0xFF
        ];

        if(res[i][2] != (val & 0xFF)) {
            res[i].push(val & 0xFF);
        }
    }
    return res;
}

function writeIndices(file, indices)
{
    for(const pair of indices)
    {
        file.write('u8', pair[0]);
        file.write('u8', pair[1]);
        file.write('u8', pair[2]);
        file.write('u8', (pair[3] === undefined) ? pair[2] : pair[3]);
    }
}

module.exports = {readIndices, writeIndices};