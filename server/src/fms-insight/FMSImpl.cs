/* Copyright (c) 2018, John Lenz

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
using BlackMaple.MachineFramework;
using Microsoft.Extensions.Configuration;
using System.Reflection;
using Microsoft.Extensions.DependencyModel;
using System.IO;
using System.Linq;
using Serilog;

namespace MachineWatchApiServer
{
    [DataContract]
    public struct FMSInfo
    {
        [DataMember] public string Name {get;set;}
        [DataMember] public string Version {get;set;}
    }

    public interface IFMSImplementation
    {
        FMSInfo Info {get;}
        IServerBackend Backend {get;}
        IList<IBackgroundWorker> Workers {get;}
    }

    public static class CurrentFMSImplementation {
      public static IFMSImplementation Impl {get;set;}
        #if DEBUG
          = new MockFMSImplementation();
        #endif
    }

#if SERVE_REMOTING
    public class ServicePlugin :
        BlackMaple.MachineWatch.RemotingServer.IMachineWatchPlugin,
        IMachineWatchVersion
    {
        private IPlugin _plugin;

        public IServerBackend serverBackend => _plugin.Backend;
        public IMachineWatchVersion serverVersion => this;
        public IEnumerable<IBackgroundWorker> workers => _plugin.Workers;
        public string Version() => _plugin.PluginInfo.Version;
        public string PluginName() => _plugin.PluginInfo.Name;

        public ServicePlugin(IPlugin p)
        {
            _plugin = p;
        }
    }
#endif

}