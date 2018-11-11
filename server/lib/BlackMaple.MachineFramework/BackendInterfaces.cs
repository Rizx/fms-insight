﻿/* Copyright (c) 2017, John Lenz

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
using BlackMaple.MachineWatchInterface;

namespace BlackMaple.MachineFramework
{
  public interface IFMSBackend : IDisposable
  {
    ILogDatabase LogDatabase();
    IJobDatabase JobDatabase();
    IJobControl JobControl();
    IInspectionControl InspectionControl();
    IOldJobDecrement OldJobDecrement();
  }

  public interface IBackgroundWorker : IDisposable
  {
  }

  public interface IFMSInstructionPath
  {
    // allows an implementation to override the algorithm which
    // finds an instruction file on disk given a part and type.
    // If this throws NotImplementedException(), the default
    // of searching for a file containing the part and type is used.
    // If this returns null or empty, a 404 error is returned to the client.
    // Otherwise, the returned string will be the target of a redirect.
    string CustomizeInstructionPath(string part, int? process, string type, long? materialID);
  }

  [DataContract]
  public class FMSNameAndVersion
  {
    [DataMember] public string Name { get; set; }
    [DataMember] public string Version { get; set; }
  }

  public class FMSImplementation
  {
    public FMSNameAndVersion NameAndVersion { get; set; }
    public IFMSBackend Backend { get; set; }
    public IList<IBackgroundWorker> Workers { get; set; } = new List<IBackgroundWorker>();
    public IFMSInstructionPath InstructionPath { get; set; } = new DefaultFMSInstrPath();

    private class DefaultFMSInstrPath : IFMSInstructionPath
    {
      public string CustomizeInstructionPath(string part, int? process, string type, long? materialID)
      {
        throw new NotImplementedException();
      }
    }
  }

}
