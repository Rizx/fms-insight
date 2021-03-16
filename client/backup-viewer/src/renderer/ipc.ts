/* Copyright (c) 2021, John Lenz

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

type Request = {
  name: string;
  id: number;
  payload: any;
};

type Response = {
  id: number;
  response?: any;
  error?: string;
};

const inFlight = new Map<number, (response: Response) => void>();
let lastId = 0;
let port: MessagePort | null = null;

window.addEventListener("message", (evt) => {
  if (evt.source === window && evt.data === "background-port") {
    port = evt.ports[0];
    port.onmessage = (msg) => {
      const response: Response = msg.data;
      const handler = inFlight.get(response.id);
      if (handler) {
        handler(response);
      }
    };
  }
});

export function sendIpc<P, R>(name: string, payload: P): Promise<R> {
  if (port === null) throw "No background port";

  const messageId = lastId;
  lastId += 1;
  const req: Request = {
    name,
    payload,
    id: messageId,
  };
  return new Promise((resolve, reject) => {
    inFlight.set(messageId, (response) => {
      inFlight.delete(messageId);
      if (response.error) {
        reject(response.error);
      } else {
        resolve(response.response);
      }
    });
    port?.postMessage(req);
  });
}
