---
name: Epic
description: Track a large body of work
title: "[EPIC] "
labels: ["epic"]
body:
  - type: markdown
    attributes:
      value: |
        ## Goal
        Describe the high level goal of this Epic.

  - type: textarea
    id: acceptance-criteria
    attributes:
      label: Acceptance Criteria
      description: What must be true for this Epic to be closed?
      placeholder: - [ ] Feature A works...
    validations:
      required: true

  - type: textarea
    id: modules
    attributes:
      label: Modules Affected
      description: Which catalogs, editors, or runtime components are affected?
      placeholder: CoreSim/TemplateCatalog, RobotStudio...

  - type: textarea
    id: tasks
    attributes:
      label: Child Tasks
      description: List of issues that belong to this Epic.
      placeholder: - [ ] #123
