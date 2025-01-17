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
import * as React from "react";
import * as ReactDOM from "react-dom";

import { CssBaseline } from "@mui/material";
import "react-vis/dist/style.css";
import "highlight.js/styles/default.css";
import "@fontsource/roboto/300.css";
import "@fontsource/roboto/400.css";
import "@fontsource/roboto/500.css";
import "@fontsource/roboto/700.css";

import { green, brown } from "@mui/material/colors";
import { ThemeProvider, createTheme } from "@mui/material/styles";
import { RecoilRoot } from "recoil";
import { Router } from "wouter";
import { enableMapSet } from "immer";
enableMapSet();

import App, { AppProps } from "./components/App";

export function render(appProps: AppProps | null, elem: HTMLElement | null): void {
  const theme = createTheme({
    palette: {
      primary: green,
      secondary: brown,
    },
  });

  ReactDOM.render(
    <ThemeProvider theme={theme}>
      <CssBaseline />
      <Router>
        <RecoilRoot>
          {/* <React.StrictMode> */}
          <App {...appProps} />
          {/* </React.StrictMode> */}
        </RecoilRoot>
      </Router>
    </ThemeProvider>,
    elem
  );
}
