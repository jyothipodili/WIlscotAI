Feature: WillScot Homepage Validation
    As a user visiting the WillScot website
    I want to verify the homepage loads and displays all critical elements correctly
    So that I can be confident the site is fully functional

    Background:
        Given I am on the WillScot homepage

    # ──────────────────────────────────────────────────────────────────────────
    # TC-001  Page Load Performance
    # ──────────────────────────────────────────────────────────────────────────
    @smoke @TC001
    Scenario: TC-001 Verify homepage loads within 4 seconds
        Then the page should have loaded within 4 seconds

    # ──────────────────────────────────────────────────────────────────────────
    # TC-002  Hero Banner Headline
    # ──────────────────────────────────────────────────────────────────────────
    @ignore  @TC002
    Scenario: TC-002 Verify hero banner displays correct headline
        Then the hero banner should display the headline "Every Link in the Chain"

    # ──────────────────────────────────────────────────────────────────────────
    # TC-003  Learn More CTA
    # ──────────────────────────────────────────────────────────────────────────
    @smoke @regression @TC003
    Scenario: TC-003 Verify Learn More CTA button is visible and clickable
        Then the "Learn more" CTA button should be visible
        And the "Learn more" CTA button should be enabled

    # ──────────────────────────────────────────────────────────────────────────
    # TC-004  Page Quality – No Errors
    # ──────────────────────────────────────────────────────────────────────────
    @regression @quality @TC004
    Scenario: TC-004 Verify no broken images console errors or JavaScript exceptions
        Then there should be no broken images on the page
        And there should be no browser console errors
        And there should be no uncaught JavaScript exceptions

    # ──────────────────────────────────────────────────────────────────────────
    # TC-005  Top-Level Navigation Items
    # ──────────────────────────────────────────────────────────────────────────
    @smoke @regression @navigation @TC005
    Scenario: TC-005 Verify top-level navigation items are visible
        Then the following navigation items should be visible
            | NavItem                  |
            | Products                 |
            | Storage Containers       |
            | Office Trailers          |
            | Browse by Use            |
            | Solutions                |
            | About Us                 |
            | Locations                |
            | Office Trailers for Sale |

    # ──────────────────────────────────────────────────────────────────────────
    # TC-006  Locations Navigation
    # ──────────────────────────────────────────────────────────────────────────
    @regression @navigation @TC006
    Scenario: TC-006 Verify clicking Locations navigates to locations page
        When I click the "Locations" navigation item
        Then the URL should contain "/en/locations"

    # ──────────────────────────────────────────────────────────────────────────
    # TC-007  Office Trailers for Sale Navigation
    # ──────────────────────────────────────────────────────────────────────────
    @regression @navigation @TC007
    Scenario: TC-007 Verify clicking Office Trailers for Sale navigates to sales showroom
        When I click the "Office Trailers for Sale" navigation item
        Then the URL should contain "/en/sales-showroom"

    # ──────────────────────────────────────────────────────────────────────────
    # TC-008  Page Title & SEO Branding
    # ──────────────────────────────────────────────────────────────────────────
    @smoke @regression @quality @TC008
    Scenario: TC-008 Verify page title contains WillScot branding
        Then the page title should contain "WillScot"

    # ──────────────────────────────────────────────────────────────────────────
    # TC-009  About Us Navigation
    # ──────────────────────────────────────────────────────────────────────────
    @regression @navigation @TC009
    Scenario: TC-009 Verify clicking About Us navigates to about page
        When I click the "About Us" navigation item
        Then the URL should contain "/en/about"

    # ──────────────────────────────────────────────────────────────────────────
    # TC-010  Request a Quote – Visibility
    # ──────────────────────────────────────────────────────────────────────────
    @smoke @regression @TC010
    Scenario: TC-010 Verify Request a Quote button is visible in header
        Then the "Request a Quote" button should be visible in the header

    # ──────────────────────────────────────────────────────────────────────────
    # TC-011  Request a Quote – Navigation
    # ──────────────────────────────────────────────────────────────────────────
    @regression @TC011
    Scenario: TC-011 Verify clicking Request a Quote opens request quote page in same tab
        When I click the "Request a Quote" button in the header
        Then the URL should contain "/en/request-quote"

    # ──────────────────────────────────────────────────────────────────────────
    # TC-012  Request Support – Navigation
    # ──────────────────────────────────────────────────────────────────────────
    @regression @TC012
    Scenario: TC-012 Verify Request Support button navigates to request service page
        When I click the "Request Support" button
        Then the URL should contain "/en/request-service"

    # ──────────────────────────────────────────────────────────────────────────
    # TC-013  Storage Containers Card – Display
    # ──────────────────────────────────────────────────────────────────────────
    @regression @products @TC013
    Scenario: TC-013 Verify Storage Containers product card displays with image and label
        Then the Storage Containers product card should display with a visible image
        And the Storage Containers product card label should read "Storage Containers"

    # ──────────────────────────────────────────────────────────────────────────
    # TC-014  Storage Containers Card – Navigation
    # ──────────────────────────────────────────────────────────────────────────
    @regression @products @TC014
    Scenario: TC-014 Verify clicking Storage Containers card navigates correctly
        When I click the Storage Containers product card
        Then the URL should contain "/en/store-secure/storage-containers"

    # ──────────────────────────────────────────────────────────────────────────
    # TC-015  Product Images – HTTP Status
    # ──────────────────────────────────────────────────────────────────────────
    @regression @products @TC015
    Scenario: TC-015 Verify product images load without distortion or broken src
        Then all product images should return HTTP 200
        And all product images should be visible on the page

    # ──────────────────────────────────────────────────────────────────────────
    # TC-016  Product Links – HTTP Status
    # ──────────────────────────────────────────────────────────────────────────
    @regression @products @TC016
    Scenario: TC-016 Verify all product links return HTTP 200
        Then all product links should return HTTP 200

    # ──────────────────────────────────────────────────────────────────────────
    # TC-017  Industry Solution Tabs
    # ──────────────────────────────────────────────────────────────────────────
    @regression @industry @TC017
    Scenario: TC-017 Verify industry solution tabs are displayed with correct names
        Then the following industry solution tabs should be displayed
            | TabName                    |
            | Construction & Builders    |
            | Education & Government     |
            | Energy & Industrial        |
            | Retail & Distribution      |
            | Manufacturing              |
            | Healthcare & Entertainment |
