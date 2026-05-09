@jenkins-demo
Feature: Jenkins Pipeline Demo Recording
    Guided browser recording of the full Jenkins CI/CD pipeline run —
    including Docker build, Minikube image load, Kubernetes deploy stages,
    and the final Allure report walkthrough.

    @jenkins-demo
    Scenario: TC-DEMO-01 Record full Jenkins CICD pipeline Docker and Kubernetes flow
        Given I open Jenkins job "WillScot-Automation" and authenticate if prompted
        And I trigger a new build and wait for it to start
        When I navigate to the pipeline view for the running build
        Then I record the pipeline progress until all stages complete or timeout after 90 minutes
        And I scroll through each pipeline stage to highlight individual results
        And I open the Allure report for the completed build and browse the results

    @ignore @jenkins-demo
    Scenario: TC-DEMO-02 Record current Jenkins pipeline build starting from login
        Given I open Jenkins job "WillScot-Automation" and authenticate if prompted
        And I load the latest build without triggering a new one
        Then I record the pipeline progress until all stages complete or timeout after 90 minutes
        And I scroll through each pipeline stage to highlight individual results
        And I open the Allure report for the completed build and browse the results
