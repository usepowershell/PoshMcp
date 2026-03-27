# Team Ceremonies

Structured meetings where agents align before or after work.

## Design Review

**Type:** Manual
**Facilitator:** Farnsworth
**Participants:** Farnsworth, Bender, Hermes, Amy, Fry
**When:** Before implementing major architectural changes
**Purpose:** Review proposed designs, identify issues, reach consensus

**Steps:**
1. Present design proposal (diagrams, interfaces, patterns)
2. Each participant reviews their domain concerns
3. Identify risks and trade-offs
4. Decide: approve, revise, or defer

**Output:** Design approval + decision record

---

## Sprint Retrospective

**Type:** Manual
**Facilitator:** Farnsworth
**Participants:** All active members
**When:** After completing a major milestone
**Purpose:** Reflect on what worked, what didn't, continuous improvement

**Steps:**
1. Review completed work
2. Each member shares: what went well, what could improve
3. Identify 2-3 actionable improvements
4. Update team process/routing if needed

**Output:** Process improvements + updated routing/decisions

---

## Implementation Kickoff

**Type:** Auto (before)
**Trigger:** Multi-agent implementation task with 3+ members
**Facilitator:** Farnsworth
**Participants:** Agents assigned to the work
**Purpose:** Align on interfaces, responsibilities, and sequencing

**Steps:**
1. Review requirements together
2. Define component boundaries and interfaces
3. Identify dependencies between agents
4. Agree on testing strategy
5. Set completion criteria

**Output:** Work plan + interface contracts

---

## Code Review Session

**Type:** Manual
**Facilitator:** Farnsworth
**Participants:** Code author + Farnsworth + relevant domain experts
**When:** Before merging significant changes
**Purpose:** Ensure code quality, architectural alignment, knowledge sharing

**Steps:**
1. Author presents changes and rationale
2. Reviewers examine code from their domain perspective
3. Discuss issues, suggestions, improvements
4. Author addresses feedback
5. Approve or request changes

**Output:** Reviewed code + approval or change requests

---

## Testing Strategy Session

**Type:** Auto (before)
**Trigger:** New feature requiring comprehensive testing
**Facilitator:** Fry
**Participants:** Fry + feature implementer(s)
**Purpose:** Define test coverage and approach before implementation

**Steps:**
1. Review feature requirements
2. Identify test scenarios (happy path, edge cases, failure modes)
3. Define test types needed (unit, integration, performance)
4. Agree on test data and mocking strategy

**Output:** Test plan + test scenarios documented
