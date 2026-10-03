/**
* @copyright 2019 - Max Bebök
* @author Max Bebök
* @license GNU-GPLv3 - see the "LICENSE" this.file in the root directory
*/

module.exports = class HKRBHelper
{
    static writeCounter(file, count)
    {
        if(count instanceof Array) {
            return count.forEach(c => HKRBHelper.writeCounter(file, c));
        }

        file.write('u32', 0);
        file.write('u32', count);
        file.write('u16', 0x8000);
        file.write('u16', count);
    }

    static writeVector4(file, data = [])
    {
        file.write('float32', data[0] || 0.0);
        file.write('float32', data[1] || 0.0);
        file.write('float32', data[2] || 0.0);
        file.write('float32', data[3] || 0.0);
    }

    static writeMatrix4x4(file, rows)
    {
        HKRBHelper.writeVector4(file, rows[0]);
        HKRBHelper.writeVector4(file, rows[1]);
        HKRBHelper.writeVector4(file, rows[2]);
        HKRBHelper.writeVector4(file, rows[3]);
    }

    static writeInverseMatrix3x4(file, matrix)
    {
        for(let column=0; column<3; ++column)
        {
            for(let row=0; row<4; ++row)
            {
                file.write('float32', matrix[row][column]);
            }
        }
    }

    static readVector4(file, removeEndingZero = true)
    {
        let res = [file.read("float32"), file.read("float32"), file.read("float32")];
        const w = file.read("float32");
        if(w != 0.0 || !removeEndingZero) {
            res.push(w);
        }
        return res;
    }

    static readCounter(file)
    {
        file.skip(4);
        const count = file.read("u32");
        file.skip(4);
        return count;
    }
};