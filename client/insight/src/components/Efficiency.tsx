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
import * as React from 'react';
import * as im from 'immutable';
import { connect } from 'react-redux';
import WorkIcon from 'material-ui-icons/Work';

import AnalysisSelectToolbar from './AnalysisSelectToolbar';
import { SelectableCycleChart } from './CycleChart';
import * as events from '../data/events';
import { Store } from '../data/store';
import * as guiState from '../data/gui-state';

export interface PartStationCycleChartProps {
  points: im.Map<string, im.Map<string, ReadonlyArray<events.StationCycle>>>;
  selected?: string;
  setSelected: (s: string) => void;
}

export function PartStationCycleChart(props: PartStationCycleChartProps) {
  return (
    <SelectableCycleChart
      select_label="Part"
      series_label="Station"
      card_label="Station Cycles"
      icon={<WorkIcon style={{color: "#6D4C41"}}/>}
      {...props}
    />
  );
}

const ConnectedPartStationCycleChart = connect(
  (st: Store) => ({
    points: st.Events.last30.station_cycles.by_part_then_stat,
    selected: st.Gui.station_cycle_selected_part
  }),
  {
    setSelected: (s: string) =>
      ({ type: guiState.ActionType.SetSelectedStationCyclePart, part: s})
  }
)(PartStationCycleChart);

export default function Efficiency() {
  return (
    <>
      <AnalysisSelectToolbar/>
      <main style={{'padding': '24px'}}>
        <ConnectedPartStationCycleChart/>
      </main>
    </>
  );
}