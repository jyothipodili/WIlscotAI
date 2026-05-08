@jenkins-demo
Feature: Jenkins Pipeline Demo Recording
    Guided browser recording of the full Jenkins CI/CD pipeline run —
    including Docker build, Minikube image load, and Kubernetes deploy stages.

    Excluded from standard test runs. To run separately:
      dotnet test --no-build --configuration Release ^
        --settings WillscotAutomation.JenkinsDemoRecording.runsettings

    @jenkins-demo
    Scenario: TC-DEMO-01 Record full Jenkins CICD pipeline Docker and Kubernetes flow
        Given I open Jenkins job "WillScot-Automation" and authenticate if prompted
        And I trigger a new build and wait for it to start
        When I navigate to the pipeline view for the running build
        Then I record the pipeline progress until all stages complete or timeout after 90 minutes
        And I scroll through each pipeline stage to highlight individual results
