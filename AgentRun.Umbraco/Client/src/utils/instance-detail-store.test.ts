import { expect } from "@open-wc/testing";
import { initialInstanceDetailState } from "./instance-detail-store.js";

describe("instance-detail-store", () => {
  it("initialInstanceDetailState returns the expected zero state", () => {
    const state = initialInstanceDetailState();
    expect(state.instance).to.be.null;
    expect(state.loading).to.be.true;
    expect(state.error).to.be.false;
    expect(state.selectedStepId).to.be.null;
    expect(state.cancelling).to.be.false;
    expect(state.runNumber).to.equal(0);
    expect(state.streaming).to.be.false;
    expect(state.providerError).to.be.false;
    expect(state.chatMessages).to.deep.equal([]);
    expect(state.streamingText).to.equal("");
    expect(state.viewingStepId).to.be.null;
    expect(state.historyMessages).to.deep.equal([]);
    expect(state.stepCompletable).to.be.false;
    expect(state.agentResponding).to.be.false;
    expect(state.retrying).to.be.false;
    expect(state.toolBatchOpen).to.be.false;
  });

  it("initialInstanceDetailState returns a fresh object each call (no shared references)", () => {
    const a = initialInstanceDetailState();
    const b = initialInstanceDetailState();
    expect(a).to.not.equal(b);
    expect(a.chatMessages).to.not.equal(b.chatMessages);
    expect(a.historyMessages).to.not.equal(b.historyMessages);
  });
});
