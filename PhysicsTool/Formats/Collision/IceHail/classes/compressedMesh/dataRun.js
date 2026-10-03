/**
* @copyright 2019 - Max Bebök
* @author Max Bebök
* @license GNU-GPLv3 - see the "LICENSE" this.file in the root directory
*/

function readDataRuns(file, count)
{
    return new Array(count).fill()
        .map(() => {
            const data = file.read("u16");
            const materialIndex = file.read("u8");
            file.skip(2);
            const count = file.read("u8");
            file.skip(2);

            return {data, materialIndex, count};
        });
}

function writeDataRuns(file, dataRuns)
{
    let index = 0;
    for(const run of dataRuns)
    {
        file.write("u16", run.data);
        file.write("u8", run.materialIndex);
        file.write("u8", 0);
        file.write("u8", index);
        file.write("u8", run.count);
        file.fill(0, 2);

        index += run.count;
    }
}

function createDataRuns(indices)
{
    const res = new Array(indices.length);
    for(let i in indices)
    {
        res[i] = {
            data: 0x59F6, // @TODO figure this out
            materialIndex: 0, // @TODO make this configurable
            count: 1
        };
    }
    return res;
}

module.exports = {readDataRuns, writeDataRuns, createDataRuns};