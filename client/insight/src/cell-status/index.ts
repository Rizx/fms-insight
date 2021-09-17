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

import {
  atom,
  selector,
  TransactionInterface_UNSTABLE,
  useRecoilTransaction_UNSTABLE,
  useSetRecoilState,
} from "recoil";
import { ICurrentStatus, IHistoricData } from "../data/api";
import * as simProd from "./sim-production";
import * as simUse from "./sim-station-use";
import * as schJobs from "./scheduled-jobs";
import * as names from "./names";
import React from "react";
import { addDays, addMonths } from "date-fns";
import { JobsBackend, LogBackend } from "../data/backend";
import { useDispatch } from "react-redux";
import * as events from "../data/events";
import { currentStatus, processEventsIntoCurrentStatus } from "./current-status";
import { conduit } from "../store/recoil-util";
import { ServerEventAndTime } from "../store/websocket";

export const onServerEvent = conduit<ServerEventAndTime>(
  (t: TransactionInterface_UNSTABLE, evt: ServerEventAndTime) => {
    if (evt.evt.logEntry) {
      t.set(currentStatus, processEventsIntoCurrentStatus(evt.evt.logEntry));
    } else if (evt.evt.newJobs) {
      simProd.updateLast30JobProduction.transform(t, evt);
      simUse.updateLast30SimStatUse.transform(t, evt);
      schJobs.updateLast30Jobs.transform(t, evt);
      names.onNewJobs(t, evt.evt.newJobs.jobs);
    } else if (evt.evt.newCurrentStatus) {
      t.set(currentStatus, evt.evt.newCurrentStatus);
    }
  }
);

const loadingCurSt = atom<boolean>({ key: "insightBackendLoadingCurSt", default: false });
const loadingJobsLast30 = atom<boolean>({ key: "insightBackendLoadingJobs", default: false });
const loadingJobsMonth = atom<boolean>({ key: "insightBackendLoadingJobsMonth", default: false });
export const loadingCellStatus = selector<boolean>({
  key: "insightBackendLoading",
  get: ({ get }) => {
    return get(loadingJobsLast30) || get(loadingJobsMonth) || get(loadingCurSt);
  },
});

function onLast30Jobs(t: TransactionInterface_UNSTABLE, historicData: Readonly<IHistoricData>): void {
  const jobsArr = Object.values(historicData.jobs);
  simUse.setLast30SimStatUse.transform(t, historicData);
  simProd.setLast30JobProduction.transform(t, historicData);
  schJobs.setLast30Jobs.transform(t, historicData);
  names.onNewJobs(t, jobsArr);
  t.set(loadingJobsLast30, false);
}

function onSpecificMonthJobs(t: TransactionInterface_UNSTABLE, historicData: Readonly<IHistoricData>): void {
  simUse.setSpecificMonthSimStatUse.transform(t, historicData);
  simProd.setSpecificMonthJobProduction.transform(t, historicData);
  schJobs.updateSpecificMonthJobs.transform(t, historicData);
  t.set(loadingJobsMonth, false);
}

export function useRefreshCellStatus(): () => Promise<void> {
  const setLoadingCurSt = useSetRecoilState(loadingCurSt);
  const handleCurrentStatus = useRecoilTransaction_UNSTABLE(
    (t) => (curSt: Readonly<ICurrentStatus>) => t.set(currentStatus, curSt),
    []
  );

  return React.useCallback(() => {
    // TODO: errors
    setLoadingCurSt(true);
    return JobsBackend.currentStatus().then(handleCurrentStatus);
  }, []);
}

export function useLoadLast30Days(): (now: Date) => Promise<void> {
  const setJobsLoading = useSetRecoilState(loadingJobsLast30);
  const setLoadingCurSt = useSetRecoilState(loadingCurSt);
  const handleHistoricData = useRecoilTransaction_UNSTABLE(
    (t) => (historicData: Readonly<IHistoricData>) => onLast30Jobs(t, historicData),
    []
  );
  const handleCurrentStatus = useRecoilTransaction_UNSTABLE(
    (t) => (curSt: Readonly<ICurrentStatus>) => {
      t.set(currentStatus, curSt);
      t.set(loadingCurSt, false);
    },
    []
  );
  const dispatch = useDispatch();

  return React.useCallback((now: Date) => {
    const thirtyDaysAgo = addDays(now, -30);

    // use ref to store latest event counter and merge useRefreshCellStatus and useLoadLast30Days?
    dispatch({
      type: events.ActionType.LoadRecentLogEntries,
      now: now,
      pledge: LogBackend.get(thirtyDaysAgo, now),
    });

    // TODO: errors
    setLoadingCurSt(true);
    const loadSt = JobsBackend.currentStatus().then(handleCurrentStatus);

    setJobsLoading(true);
    const loadJob = JobsBackend.history(thirtyDaysAgo, now).then(handleHistoricData);

    return Promise.all([loadSt, loadJob]).then(() => void 0);
  }, []);
}

export function useLoadSpecificMonth(): (month: Date) => void {
  const setJobsLoading = useSetRecoilState(loadingJobsMonth);
  const handleHistoricData = useRecoilTransaction_UNSTABLE(
    (t) => (historicData: Readonly<IHistoricData>) => onSpecificMonthJobs(t, historicData),
    []
  );
  const dispatch = useDispatch();

  return React.useCallback((month: Date) => {
    const startOfNextMonth = addMonths(month, 1);

    dispatch({
      type: events.ActionType.LoadSpecificMonthLogEntries,
      month: month,
      pledge: LogBackend.get(month, startOfNextMonth),
    });

    // TODO: errors

    setJobsLoading(true);
    void JobsBackend.history(month, startOfNextMonth)
      .then(handleHistoricData)
      .finally(() => setJobsLoading(false));
  }, []);
}
