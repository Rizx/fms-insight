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
const {
  app,
  BrowserWindow,
  Menu,
  ipcMain,
  shell,
  dialog,
  MessageChannelMain,
  protocol,
  session,
} = require("electron");
const path = require("path");

app.on("web-contents-created", (_, contents) => {
  contents.on("will-navigate", (event, url) => {
    if (url.startsWith("https://fms-insight.seedtactics.com")) {
      shell.openExternal(url);
    }
    event.preventDefault();
  });
  contents.setWindowOpenHandler((details) => {
    if (details.url.startsWith("https://fms-insight.seedtactics.com")) {
      shell.openExternal(url);
    }
    return { action: "deny" };
  });
});

const insightHost = "backup://insight/";

protocol.registerSchemesAsPrivileged([
  { scheme: "backup", privileges: { standard: true } },
]);

app.on("ready", () => {
  Menu.setApplicationMenu(null);

  const { port1, port2 } = new MessageChannelMain();

  const background = new BrowserWindow({
    show: false,
    webPreferences: {
      nodeIntegration: true,
      contextIsolation: false,
    },
  });
  background.loadFile("build/background/background.html");

  background.webContents.on("did-finish-load", () => {
    background.webContents.postMessage("communication-port", null, [port2]);
  });

  const ses = session.fromPartition("insight-main-window");
  ses.protocol.registerFileProtocol("backup", (request, callback) => {
    if (request.url === insightHost + "backup/open") {
      callback({
        path: path.join(app.getAppPath(), "build", "insight", "index.html"),
      });
    } else if (request.url.startsWith(insightHost)) {
      const url = request.url.substr(insightHost.length);
      callback({
        path: path.join(app.getAppPath(), "build", "insight", url),
      });
    } else {
      console.log("Invalid host for " + request.url);
      callback({ error: -6 });
    }
  });
  ses.webRequest.onHeadersReceived((details, callback) => {
    if (details.url.startsWith("devtools")) {
      callback({});
    } else {
      callback({
        responseHeaders: {
          ...details.responseHeaders,
          "Content-Security-Policy": [
            // will produce error because of plugin-legacy https://github.com/vitejs/vite/tree/main/packages/plugin-legacy#content-security-policy,
            // but don't care because running in electron
            "default-src 'self'; style-src 'self' 'unsafe-inline'; connect-src 'none'; base-uri 'self'; form-action 'self'; font-src 'self' data:; manifest-src 'self' data:; ",
          ],
        },
      });
    }
  });

  const mainWindow = new BrowserWindow({
    height: 600,
    width: 800,
    webPreferences: {
      nodeIntegration: false,
      contextIsolation: true,
      session: ses,
      preload: path.join(app.getAppPath(), "src", "preload.js"),
    },
  });
  mainWindow.maximize();

  mainWindow.webContents.on("did-finish-load", () => {
    mainWindow.webContents.postMessage(
      "insight-background-communication-port",
      null,
      [port1]
    );
  });

  mainWindow.webContents.on("before-input-event", (e, i) => {
    if (i.type === "keyDown" && i.key === "F12") {
      mainWindow.webContents.openDevTools({ mode: "detach" });
      background.webContents.openDevTools({ mode: "detach" });
    }
    if (i.type === "keyDown" && i.key === "F5") {
      mainWindow.reload();
      background.reload();
    }
  });

  mainWindow.on("closed", () => {
    app.quit();
  });

  ipcMain.handle("open-insight-file", async (_) => {
    const paths = await dialog.showOpenDialog(mainWindow, {
      title: "Open Backup Database File",
      properties: ["openFile"],
    });
    if (!paths.canceled && paths.filePaths.length > 0) {
      return paths.filePaths[0];
    } else {
      return null;
    }
  });

  mainWindow.loadURL(insightHost + "backup/open");
});
