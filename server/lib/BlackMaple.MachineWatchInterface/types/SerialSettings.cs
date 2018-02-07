/* Copyright (c) 2017, John Lenz

All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

    * Redistributions of source code must retain the above copyright
      notice, this list of conditions and the following disclaimer.

    * Redistributions in binary form must reproduce the above
      copyright notice, this list of conditions and the following
      disclaimer in the documentation and/or other materials provided
      with the distribution.

    * Neither the name of John Lenz, Black Maple Software, SeedTactics,
      nor the names of other contributors may be used to endorse or
      promote products derived from this software without specific
      prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
"AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace BlackMaple.MachineWatchInterface
{
    [Serializable, DataContract]
    public enum SerialType
    {
        [EnumMember] NoSerials,
        [EnumMember] OneSerialPerMaterial,  // assign a different serial to each piece of material
        [EnumMember] OneSerialPerCycle,     // assign a single serial to all the material on each cycle
        [EnumMember] SerialDeposit          // deposit serial into machine file to be scribed on part
    }

    [Serializable, DataContract]
    public class SerialSettings
    {
        [DataMember(IsRequired=true)] public SerialType SerialType {get;}
        [DataMember(IsRequired=true)] public int SerialLength {get;}

        //settings only for serial deposit
        [DataMember(IsRequired=false, EmitDefaultValue=false)] public int DepositOnProcess {get;}
        [DataMember(IsRequired=false, EmitDefaultValue=false)] public string FilenameTemplate {get;}
        [DataMember(IsRequired=false, EmitDefaultValue=false)] public string ProgramTemplate {get;}

        public SerialSettings(SerialType t, int len)
        {
            SerialType = t;
            SerialLength = len;
            DepositOnProcess = 1;
            FilenameTemplate = null;
            ProgramTemplate = null;
        }
        public SerialSettings(int len, int proc, string fileTemplate, string progTemplate)
        {
            SerialType = SerialType.SerialDeposit;
            SerialLength = len;
            DepositOnProcess = proc;
            FilenameTemplate = fileTemplate;
            ProgramTemplate = progTemplate;
        }
    }
}
