/* Copyright (c) 2019, John Lenz

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
import SearchIcon from "@mui/icons-material/Search";
import { Card } from "@mui/material";
import { CardContent } from "@mui/material";
import { CardHeader } from "@mui/material";
import { Select } from "@mui/material";
import { MenuItem } from "@mui/material";
import { Sankey, Hint } from "react-vis";
import { Tooltip } from "@mui/material";
import { IconButton } from "@mui/material";
import ImportExport from "@mui/icons-material/ImportExport";

import { PartIdenticon } from "../station-monitor/Material";
import { SankeyNode, SankeyDiagram, inspectionDataToSankey } from "../../data/inspection-sankey";

import { PartAndInspType, InspectionLogEntry } from "../../cell-status/inspections";
import { HashMap } from "prelude-ts";
import InspectionDataTable from "./InspectionDataTable";
import { copyInspectionEntriesToClipboard } from "../../data/results.inspection";
import { DataTableActionZoomType } from "./DataTable";
import { useIsDemo } from "../routes";

interface InspectionSankeyDiagramProps {
  readonly sankey: SankeyDiagram;
}

// d3 adds some properties to each node
interface D3SankeyNode extends SankeyNode {
  readonly x0: number;
  readonly y0: number;
  readonly x1: number;
  readonly y1: number;
}

// d3 adds some properties to each link
interface D3Link {
  readonly source: D3SankeyNode;
  readonly target: D3SankeyNode;
  readonly value: number;

  readonly index: number;
  readonly x0: number;
  readonly y0: number;
  readonly x1: number;
  readonly y1: number;
}

interface InspectionSankeyDiagramState {
  readonly activeLink?: D3Link;
  readonly chartHeight: number;
  readonly chartWidth: number;
}

class InspectionSankeyDiagram extends React.PureComponent<InspectionSankeyDiagramProps, InspectionSankeyDiagramState> {
  state: InspectionSankeyDiagramState = { chartHeight: 500, chartWidth: 600 };

  renderHint() {
    const { activeLink } = this.state;
    if (!activeLink) {
      return;
    }

    // calculate center x,y position of link for positioning of hint
    const x = activeLink.source.x1 + (activeLink.target.x0 - activeLink.source.x1) / 2;
    const y = activeLink.y0 - (activeLink.y0 - activeLink.y1) / 2;

    const hintValue = {
      [`${activeLink.source.name} ➞ ${activeLink.target.name}`]: activeLink.value.toString() + " parts",
    };

    return <Hint x={x} y={y} value={hintValue} />;
  }

  componentDidMount() {
    this.setState({
      chartHeight: window.innerHeight - 200,
      chartWidth: window.innerWidth - 300,
    });
  }

  render() {
    // d3-sankey mutates nodes and links array, so create copy
    return (
      <Sankey
        nodes={this.props.sankey.nodes.map((d) => ({ ...d }))}
        links={this.props.sankey.links.map((l, idx) => ({
          ...l,
          opacity: this.state.activeLink && this.state.activeLink.index === idx ? 0.6 : 0.3,
        }))}
        width={this.state.chartWidth}
        height={this.state.chartHeight}
        onLinkMouseOver={(link: D3Link) => this.setState({ activeLink: link })}
        onLinkMouseOut={() => this.setState({ activeLink: undefined })}
      >
        {this.state.activeLink ? this.renderHint() : undefined}
      </Sankey>
    );
  }
}

// use purecomponent to only recalculate the SankeyDiagram when the InspectionData changes.
class ConvertInspectionDataToSankey extends React.PureComponent<{
  data: ReadonlyArray<InspectionLogEntry>;
}> {
  render() {
    return <InspectionSankeyDiagram sankey={inspectionDataToSankey(this.props.data)} />;
  }
}

export interface InspectionSankeyProps {
  readonly inspectionlogs: HashMap<PartAndInspType, ReadonlyArray<InspectionLogEntry>>;
  readonly default_date_range: Date[];
  readonly zoomType?: DataTableActionZoomType;
  readonly subtitle?: string;
  readonly restrictToPart?: string;
  readonly defaultToTable: boolean;
  readonly extendDateRange?: (numDays: number) => void;
  readonly hideOpenDetailColumn?: boolean;
}

export function InspectionSankey(props: InspectionSankeyProps) {
  const demo = useIsDemo();
  const [curPart, setSelectedPart] = React.useState<string | undefined>(demo ? "aaa" : undefined);
  const [selectedInspectType, setSelectedInspectType] = React.useState<string | undefined>(demo ? "CMM" : undefined);
  const [showTable, setShowTable] = React.useState<boolean>(props.defaultToTable);

  let curData: ReadonlyArray<InspectionLogEntry> | undefined;
  const selectedPart = props.restrictToPart || curPart;
  if (selectedPart && selectedInspectType) {
    curData = props.inspectionlogs.get(new PartAndInspType(selectedPart, selectedInspectType)).getOrElse([]);
  }
  const parts = props.inspectionlogs
    .keySet()
    .map((x) => x.part)
    .toArray({ sortOn: (x) => x });
  const inspTypes = props.inspectionlogs
    .keySet()
    .map((e) => e.inspType)
    .toArray({ sortOn: (x) => x });
  return (
    <Card raised>
      <CardHeader
        title={
          <div
            style={{
              display: "flex",
              flexWrap: "wrap",
              alignItems: "center",
            }}
          >
            <SearchIcon style={{ color: "#6D4C41" }} />
            <div style={{ marginLeft: "10px", marginRight: "3em" }}>Inspections</div>
            <div style={{ flexGrow: 1 }} />
            {curData ? (
              <Tooltip title="Copy to Clipboard">
                <IconButton
                  onClick={() =>
                    curData
                      ? copyInspectionEntriesToClipboard(selectedPart || "", selectedInspectType || "", curData)
                      : undefined
                  }
                  style={{ height: "25px", paddingTop: 0, paddingBottom: 0 }}
                  size="large"
                >
                  <ImportExport />
                </IconButton>
              </Tooltip>
            ) : undefined}
            <Select
              autoWidth
              value={showTable ? "table" : "sankey"}
              onChange={(e) => setShowTable(e.target.value === "table")}
            >
              <MenuItem key="sankey" value="sankey">
                Sankey
              </MenuItem>
              <MenuItem key="table" value="table">
                Table
              </MenuItem>
            </Select>
            <Select
              name="inspection-sankey-select-type"
              autoWidth
              displayEmpty
              style={{ marginRight: "1em", marginLeft: "1em" }}
              value={selectedInspectType || ""}
              onChange={(e) => setSelectedInspectType(e.target.value as string)}
            >
              {selectedInspectType ? undefined : (
                <MenuItem key={0} value="">
                  <em>Select Inspection Type</em>
                </MenuItem>
              )}
              {inspTypes.map((n) => (
                <MenuItem key={n} value={n}>
                  {n}
                </MenuItem>
              ))}
            </Select>
            {props.restrictToPart === undefined ? (
              <Select
                name="inspection-sankey-select-part"
                autoWidth
                displayEmpty
                value={selectedPart || ""}
                onChange={(e) => setSelectedPart(e.target.value as string)}
              >
                {selectedPart ? undefined : (
                  <MenuItem key={0} value="">
                    <em>Select Part</em>
                  </MenuItem>
                )}
                {parts.map((n) => (
                  <MenuItem key={n} value={n}>
                    <div style={{ display: "flex", alignItems: "center" }}>
                      <PartIdenticon part={n} size={30} />
                      <span style={{ marginRight: "1em" }}>{n}</span>
                    </div>
                  </MenuItem>
                ))}
              </Select>
            ) : undefined}
          </div>
        }
        subheader={props.subtitle}
      />
      <CardContent>
        <div style={{ display: "flex", justifyContent: "center" }}>
          {curData ? (
            showTable ? (
              <InspectionDataTable
                zoomType={props.zoomType}
                points={curData}
                default_date_range={props.default_date_range}
                extendDateRange={props.extendDateRange}
                hideOpenDetailColumn={props.hideOpenDetailColumn}
              />
            ) : (
              <ConvertInspectionDataToSankey data={curData} />
            )
          ) : undefined}
        </div>
      </CardContent>
    </Card>
  );
}
