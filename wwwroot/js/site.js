// Subnet tree functionality
$(document).ready(function () {
    // Initialize all subnet children as visible (fully expanded)
    $('.subnet-children').show();
    
    // Update toggle icons
    updateToggleIcons();
    
    // Toggle subnet children visibility
    $('.subnet-toggle').on('click', function () {
        var $children = $(this).closest('.subnet-item').children('.subnet-children');
        $children.slideToggle(200);
        
        // Update the toggle icon
        updateToggleIcons();
    });
    
    // Expand all subnets
    $('#expand-all').on('click', function () {
        $('.subnet-children').slideDown(200);
        updateToggleIcons();
    });
    
    // Collapse all subnets
    $('#collapse-all').on('click', function () {
        // Keep the first level visible
        $('.subnet-tree > .subnet-item > .subnet-children').show();
        $('.subnet-tree .subnet-item .subnet-item > .subnet-children').slideUp(200);
        updateToggleIcons();
    });
    
    // Function to update toggle icons
    function updateToggleIcons() {
        $('.subnet-toggle').each(function () {
            var $children = $(this).closest('.subnet-item').children('.subnet-children');
            if ($children.is(':visible')) {
                $(this).html('<i class="bi bi-dash-square"></i>');
            } else {
                $(this).html('<i class="bi bi-plus-square"></i>');
            }
        });
    }
    
    // Only show toggle button if there are children
    $('.subnet-item').each(function () {
        var $children = $(this).children('.subnet-children');
        var $toggle = $(this).children('.subnet-toggle');
        
        if ($children.children().length === 0) {
            $toggle.html('<i class="bi bi-dash"></i>');
            $toggle.css('cursor', 'default');
            $toggle.off('click');
        }
    });
});
